using AffiVideo.Application;
using AffiVideo.Application.Generation;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Generation;

// No method names an Organization: the context's filter leaves only the caller's
// Projects, Variants, Storyboards and Stills.
internal sealed class ScopedGeneration(
    AffiVideoDbContext database, Caller caller, IObjectStorage storage, IOptions<GenerationOptions> generation,
    IOptions<ProductionCostOptions> costs, TimeProvider clock) : IGeneration
{
    private const string NoImageProvider =
        "Generation is unavailable: no image provider is configured, so nothing can be generated and a Hybrid video is planned as a Product Lock one.";

    public async Task<GenerationEstimate?> EstimateAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken)
    {
        if (await FindStoryboardAsync(projectId, variantId, version, cancellationToken) is not { } storyboard) return null;
        return await EstimateAsync(projectId, storyboard, cancellationToken);
    }

    public async Task<GenerationConfirmation?> ConfirmAsync(
        Guid projectId, Guid variantId, int version, decimal confirmedTotal, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (await FindStoryboardAsync(projectId, variantId, version, cancellationToken) is not { } storyboard) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Still is queued for the caller's Organization, and there is no caller.");
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can confirm a generation.");
        await using var deciding = await DecideForProjectAsync(projectId, cancellationToken);
        if (deciding is null) return null;
        if (await FindByKeyAsync(projectId, idempotencyKey, cancellationToken) is { Count: > 0 } already)
        {
            return new GenerationConfirmation(already, Queued: false, null);
        }

        var estimate = await EstimateAsync(projectId, storyboard, cancellationToken);
        if (estimate.Unavailable is { } unavailable) return GenerationConfirmation.Refuse(unavailable);
        if (estimate.Stills.Count == 0)
        {
            return GenerationConfirmation.Refuse(
                "There is nothing to generate for this Storyboard version: no Scene of it is generated, or each already has its Still.");
        }
        if (confirmedTotal != estimate.Total)
        {
            return GenerationConfirmation.Refuse(
                $"The estimate is {Money(estimate.Total)} {estimate.Currency}, and {Money(confirmedTotal)} was confirmed. " +
                "Nothing was generated. Look at the estimate again and confirm it.");
        }
        if (GenerationCeilings.Refusal(estimate.Spent, estimate.Total, estimate.Ceiling, estimate.Currency) is { } pastCeiling)
        {
            return GenerationConfirmation.Refuse($"{pastCeiling} {RaiseTheCeiling}");
        }

        var productId = await database.Projects.Where(p => p.Id == projectId).Select(p => p.ProductId).SingleAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var stills = estimate.Stills
            .Select((asked, index) => new Still(
                Guid.CreateVersion7(), organizationId, projectId, productId, asked.Technique, asked.Description, estimate.ImageProvider,
                new EstimatedAmount(asked.EstimatedAmount, estimate.Currency), costs.Value.RatesVersion, idempotencyKey, index + 1, member, now))
            .ToList();
        database.Stills.AddRange(stills);
        await database.SaveChangesAsync(cancellationToken);
        await deciding.CommitAsync(cancellationToken);
        // Nothing has been made yet, so none has a cost.
        return new GenerationConfirmation([.. stills.Select(still => new StillRecord(still, null))], Queued: true, null);
    }

    public async Task<Page<StillRecord>?> ListStillsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await database.Projects.AnyAsync(p => p.Id == projectId, cancellationToken)) return null;

        var all = database.Stills.AsNoTracking().Where(s => s.ProjectId == projectId);
        var items = await all
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<StillRecord>(await WithCostsAsync(items, cancellationToken), page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<StillRecord?> FindStillAsync(Guid stillId, CancellationToken cancellationToken) =>
        await database.Stills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken) is { } still
            ? (await WithCostsAsync([still], cancellationToken))[0]
            : null;

    public async Task<StillDecision?> ApproveStillAsync(Guid stillId, CancellationToken cancellationToken)
    {
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can approve a Still.");
        var still = await database.Stills.SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        if (still is null) return null;
        if (!still.Approve(member, clock.GetUtcNow())) return StillDecision.Refuse(NotToBeDecided(still, "approved"));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StillDecision.Refuse("Someone else decided about this Still at the same moment. Look at it again.");
        }
        return new StillDecision((await WithCostsAsync([still], cancellationToken))[0], Queued: false, null);
    }

    public async Task<StillDecision?> AnotherStillAsync(
        Guid stillId, decimal confirmedAmount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can ask for another Still.");
        if (await database.Stills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken) is not { } found) return null;
        if (found.ProjectId is not { } projectId)
        {
            return StillDecision.Refuse("The Project this Still was made for has been deleted, so there is nothing to make another for.");
        }
        // The Still is read for the decision only once its Project's generation is this request's to decide.
        await using var deciding = await DecideForProjectAsync(projectId, cancellationToken);
        var still = await database.Stills.SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        if (deciding is null || still is null) return null;
        if (await FindByKeyAsync(projectId, idempotencyKey, cancellationToken) is [var already, ..])
        {
            return new StillDecision(already, Queued: false, null);
        }
        if (!generation.Value.CanGenerateStills) return StillDecision.Refuse(NoImageProvider);

        var now = clock.GetUtcNow();
        if (!still.State.CanBecome(StillState.Rejected)) return StillDecision.Refuse(NotToBeDecided(still, "replaced by another"));
        var provider = generation.Value.ImageProvider;
        var amount = new EstimatedAmount(costs.Value.StillRateOf(provider), costs.Value.Currency);
        if (confirmedAmount != amount.Amount)
        {
            return StillDecision.Refuse(
                $"Another Still is estimated at {Money(amount.Amount)} {amount.Currency}, and {Money(confirmedAmount)} was confirmed. " +
                "Nothing was generated. Look at the amount again and confirm it.");
        }
        // Another take is paid for like the first, and the one it replaces stays paid for.
        if (GenerationCeilings.Refusal(await SpentAsync(projectId, cancellationToken), amount.Amount, await CeilingAsync(cancellationToken), amount.Currency)
            is { } pastCeiling)
        {
            return StillDecision.Refuse($"{pastCeiling} {RaiseTheCeiling}");
        }

        still.Reject(now);
        var another = new Still(
            Guid.CreateVersion7(), still.OrganizationId, projectId, still.ProductId, still.Technique, still.Description, provider,
            amount, costs.Value.RatesVersion, idempotencyKey, 1, member, now);
        database.Stills.Add(another);
        try
        {
            // The rejection and the Still that replaces it are saved together, or neither is.
            await database.SaveChangesAsync(cancellationToken);
            await deciding.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // An approval made at the same moment: that is not a generation, so it does not wait its turn.
            return StillDecision.Refuse("Someone else decided about this Still at the same moment. Look at it again.");
        }
        return new StillDecision(new StillRecord(another, null), Queued: true, null);
    }

    // Why a Still in this state cannot be approved or replaced.
    private static string NotToBeDecided(Still still, string decision) => still.State switch
    {
        StillState.Queued or StillState.Generating => $"This Still has not been made yet, so it cannot be {decision}. Wait for it.",
        StillState.Approved => $"This Still is already approved, so it cannot be {decision}.",
        StillState.Rejected => $"Another Still was asked for in place of this one, so it cannot be {decision}.",
        _ => $"This Still was not made, so it cannot be {decision}. Confirm the estimate again to ask for it.",
    };

    public async Task<Stream?> OpenStillAsync(Guid stillId, CancellationToken cancellationToken)
    {
        var still = await database.Stills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        return still is { HasPicture: true } ? await storage.OpenAsync(still.StorageKey, cancellationToken) : null;
    }

    private async Task<IReadOnlyList<StillRecord>> WithCostsAsync(IReadOnlyList<Still> stills, CancellationToken cancellationToken)
    {
        var ids = stills.Select(still => (Guid?)still.Id).ToList();
        var costs = await database.GenerationCostRecords.AsNoTracking()
            .Where(cost => ids.Contains(cost.StillId))
            .ToDictionaryAsync(cost => cost.StillId!.Value, cancellationToken);
        return [.. stills.Select(still => new StillRecord(still, costs.GetValueOrDefault(still.Id)))];
    }

    private async Task<GenerationEstimate> EstimateAsync(Guid projectId, Storyboard storyboard, CancellationToken cancellationToken)
    {
        var ceiling = await CeilingAsync(cancellationToken);
        var ofProject = await database.Stills.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(cancellationToken);
        var spent = Spent(ofProject);
        var provider = generation.Value.ImageProvider;
        var currency = costs.Value.Currency;

        // A Scene has the Project's Still of the picture it describes, whichever Variant asked for it first.
        var generated = storyboard.Scenes.Where(scene => scene.Technique == Technique.GeneratedBackdrop).ToList();
        var has = generated
            .Select(scene => (Scene: scene, Still: ofProject
                .Where(still => still.IsCurrent && still.Technique == scene.Technique && still.Description == scene.Description)
                .OrderByDescending(still => still.CreatedAt).ThenByDescending(still => still.Id)
                .FirstOrDefault()))
            .ToList();
        var records = await WithCostsAsync([.. has.Where(found => found.Still is not null).Select(found => found.Still!).Distinct()], cancellationToken);
        var sceneStills = has
            .Where(found => found.Still is not null)
            .Select(found => new SceneStill(found.Scene.Position, records.Single(record => record.Still.Id == found.Still!.Id)))
            .ToList();
        if (!generation.Value.CanGenerateStills)
        {
            return new GenerationEstimate(NoImageProvider, [], 0m, currency, spent, ceiling, provider, sceneStills, AnotherStillAmount: null);
        }

        var rate = costs.Value.StillRateOf(provider);
        var stills = has
            .Where(found => found.Still is null)
            // Two Scenes that describe the same picture share its Still.
            .DistinctBy(found => (found.Scene.Technique, found.Scene.Description))
            .Select(found => new EstimatedStill(found.Scene.Position, found.Scene.Technique, found.Scene.Description ?? "", rate))
            .ToList();
        return new GenerationEstimate(
            null, stills, stills.Sum(still => still.EstimatedAmount), currency, spent, ceiling, provider, sceneStills, AnotherStillAmount: rate);
    }

    // What a Project's generation may spend, and which of its Scenes have a Still, are read and then
    // written. Two requests doing that at the same moment would each see what the other is about to
    // change: the same Scene paid for twice, or the ceiling passed between them. So every request that
    // may queue a Still for a Project holds the Project's row until it has: one decides at a time, and
    // the same click arriving twice finds what its first arrival queued. Disposing without committing
    // keeps nothing. Null when the Project was deleted after it was read.
    private async Task<IDbContextTransaction?> DecideForProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var held = await database.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM "Projects" WHERE "Id" = {projectId} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        if (held.Count == 1) return transaction;
        await transaction.DisposeAsync();
        return null;
    }

    private const string RaiseTheCeiling = "An Owner can raise the Organization's ceiling.";

    // The ceiling is the Organization's, and applies to each of its Projects.
    private Task<decimal> CeilingAsync(CancellationToken cancellationToken) =>
        database.Organizations.Where(o => o.Id == caller.OrganizationId).Select(o => o.GenerationCeiling).SingleAsync(cancellationToken);

    private async Task<decimal> SpentAsync(Guid projectId, CancellationToken cancellationToken) =>
        Spent(await database.Stills.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(cancellationToken));

    // What is on its way counts as spent already: it was confirmed, and will be charged for.
    private static decimal Spent(IEnumerable<Still> ofProject) =>
        ofProject.Where(still => still.CountsAsSpent).Sum(still => still.EstimatedAmount);

    private async Task<IReadOnlyList<StillRecord>> FindByKeyAsync(Guid projectId, string idempotencyKey, CancellationToken cancellationToken) =>
        await WithCostsAsync(
            await database.Stills.AsNoTracking()
                .Where(s => s.ProjectId == projectId && s.IdempotencyKey == idempotencyKey)
                .OrderBy(s => s.Ordinal)
                .ToListAsync(cancellationToken),
            cancellationToken);

    // The version has to be this Variant's, and the Variant this Project's: an
    // identifier from one does not work under another.
    private async Task<Storyboard?> FindStoryboardAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken)
    {
        if (!await database.Variants.AnyAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken)) return null;
        return await database.Storyboards.AsNoTracking()
            .SingleOrDefaultAsync(s => s.VariantId == variantId && s.Version == version, cancellationToken);
    }

    private static string Money(decimal amount) => GenerationCeilings.Money(amount);
}
