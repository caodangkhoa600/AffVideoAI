using AffiVideo.Application;
using AffiVideo.Application.Generation;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Generation;

// No method names an Organization: the context's filter leaves only the caller's
// Projects, Variants, Storyboards, Stills and Clips.
internal sealed class ScopedGeneration(
    AffiVideoDbContext database, Caller caller, IObjectStorage storage, IOptions<GenerationOptions> generation,
    IOptions<ProductionCostOptions> costs, TimeProvider clock) : IGeneration
{
    private const string NoImageProvider =
        "Generation is unavailable: no image provider is configured, so nothing can be generated and a Hybrid video is planned as a Product Lock one.";

    private const string NoVideoProvider =
        "Generation is unavailable: this Storyboard version has generated footage, and no video provider is configured to make its Clip with.";

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
        await using var deciding = await database.HoldProjectAsync(projectId, cancellationToken);
        if (deciding is null) return null;
        if (await FindByKeyAsync(projectId, idempotencyKey, cancellationToken) is var already && already.Stills.Count + already.Clips.Count > 0)
        {
            return new GenerationConfirmation(already.Stills, already.Clips, Queued: false, null);
        }

        var estimate = await EstimateAsync(projectId, storyboard, cancellationToken);
        if (estimate.Unavailable is { } unavailable) return GenerationConfirmation.Refuse(unavailable);
        if (estimate.Stills.Count == 0 && estimate.Clips.Count == 0)
        {
            return GenerationConfirmation.Refuse(
                "There is nothing to generate for this Storyboard version: no Scene of it is generated, or each already has its Still and its Clip.");
        }
        if (confirmedTotal != estimate.Total)
        {
            return GenerationConfirmation.Refuse(
                $"The estimate is {Money(estimate.Total)} {estimate.Currency}, and {Money(confirmedTotal)} was confirmed. " +
                "Nothing was generated. Look at the estimate again and confirm it.");
        }
        // A Clip confirmed before, whose Still was not made, is on its way again with the Still asked for here.
        if (GenerationCeilings.Refusal(estimate.Spent, estimate.Total + estimate.Resumed, estimate.Ceiling, estimate.Currency) is { } pastCeiling)
        {
            return GenerationConfirmation.Refuse($"{pastCeiling} {RaiseTheCeiling}");
        }

        var productId = await database.Projects.Where(p => p.Id == projectId).Select(p => p.ProductId).SingleAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var stills = estimate.Stills
            .Select((asked, index) => new Still(
                Guid.CreateVersion7(), organizationId, projectId, productId, asked.Technique, asked.Description, asked.FactIds,
                estimate.ImageProvider, new EstimatedAmount(asked.EstimatedAmount, estimate.Currency), costs.Value.RatesVersion,
                idempotencyKey, index + 1, member, now))
            .ToList();
        // The one confirmation covers the Clips too (ADR 0003). One whose Still is not approved yet waits for it.
        var clips = estimate.Clips
            .Select((asked, index) => new Clip(
                Guid.CreateVersion7(), organizationId, projectId, productId, asked.StillId, asked.Description, asked.FactIds, asked.DurationMs,
                estimate.VideoProvider, new EstimatedAmount(asked.EstimatedAmount, estimate.Currency), costs.Value.RatesVersion,
                idempotencyKey, index + 1, member, now))
            .ToList();
        database.Stills.AddRange(stills);
        database.Clips.AddRange(clips);
        await database.SaveChangesAsync(cancellationToken);
        await deciding.CommitAsync(cancellationToken);
        // Nothing has been made yet, so none has a cost.
        return new GenerationConfirmation(
            [.. stills.Select(still => new StillRecord(still, null))], [.. clips.Select(clip => new ClipRecord(clip, null))], Queued: true, null);
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
        if (await database.Stills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken) is not { } found) return null;
        // The Still is read for the decision only once its Project's Storyboards are this request's to add to.
        // A Still whose Project was deleted is kept: it is approved with no Storyboard to refer to it.
        await using var deciding = found.ProjectId is { } projectId ? await database.HoldProjectAsync(projectId, cancellationToken) : null;
        var still = await database.Stills.SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        if (still is null) return null;
        var now = clock.GetUtcNow();
        if (!still.Approve(member, now)) return StillDecision.Refuse(NotToBeDecided(still, "approved"));
        try
        {
            // A Clip is made only from an approved Still: those confirmed with this one were waiting for this.
            foreach (var clip in await ClipsWaitingForAsync(still, cancellationToken)) clip.StartFrom(still.Id, now);
            // The approval and the versions that refer to the Still are kept together, or neither is.
            await database.SaveChangesAsync(cancellationToken);
            if (still.ProjectId is { } ofProject) database.Storyboards.AddRange(await VersionsReferringAsync(ofProject, now, cancellationToken));
            await database.SaveChangesAsync(cancellationToken);
            if (deciding is not null) await deciding.CommitAsync(cancellationToken);
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
        await using var deciding = await database.HoldProjectAsync(projectId, cancellationToken);
        var still = await database.Stills.SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        if (deciding is null || still is null) return null;
        if ((await FindByKeyAsync(projectId, idempotencyKey, cancellationToken)).Stills is [var already, ..])
        {
            return new StillDecision(already, Queued: false, null);
        }
        if (!generation.Value.CanGenerateStills) return StillDecision.Refuse(NoImageProvider);

        var now = clock.GetUtcNow();
        if (!still.State.CanBecome(StillState.Rejected)) return StillDecision.Refuse(NotToBeDecided(still, "replaced by another"));
        // A Clip starts from one Still. Once it has, another Still would be paid for and shown nowhere.
        if (await database.Clips.AnyAsync(c => c.StillId == stillId && c.State != ClipState.Failed, cancellationToken))
        {
            return StillDecision.Refuse(
                "A Clip was made from this Still, or is being made, so another Still would change nothing in the video. " +
                "To have other footage, describe another picture in the Scene.");
        }
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
            Guid.CreateVersion7(), still.OrganizationId, projectId, still.ProductId, still.Technique, still.Description, still.FactIds,
            provider, amount, costs.Value.RatesVersion, idempotencyKey, 1, member, now);
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

    public async Task<Stream?> OpenStillAsync(Guid stillId, CancellationToken cancellationToken)
    {
        var still = await database.Stills.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stillId, cancellationToken);
        return still is { HasPicture: true } ? await storage.OpenAsync(still.StorageKey, cancellationToken) : null;
    }

    public async Task<Page<ClipRecord>?> ListClipsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await database.Projects.AnyAsync(p => p.Id == projectId, cancellationToken)) return null;

        var all = database.Clips.AsNoTracking().Where(c => c.ProjectId == projectId);
        var items = await all
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<ClipRecord>(await WithCostsAsync(items, cancellationToken), page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<ClipRecord?> FindClipAsync(Guid clipId, CancellationToken cancellationToken) =>
        await database.Clips.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clipId, cancellationToken) is { } clip
            ? (await WithCostsAsync([clip], cancellationToken))[0]
            : null;

    public async Task<ClipDecision?> ApproveClipAsync(Guid clipId, CancellationToken cancellationToken)
    {
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can approve a Clip.");
        if (await database.Clips.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clipId, cancellationToken) is not { } found) return null;
        // As for a Still: the Clip is read for the decision only once its Project's Storyboards are this request's to add to.
        await using var deciding = found.ProjectId is { } projectId ? await database.HoldProjectAsync(projectId, cancellationToken) : null;
        var clip = await database.Clips.SingleOrDefaultAsync(c => c.Id == clipId, cancellationToken);
        if (clip is null) return null;
        var now = clock.GetUtcNow();
        if (!clip.Approve(member, now))
        {
            return ClipDecision.Refuse(clip.State switch
            {
                ClipState.WaitingForStill => "This Clip has not been made yet: it is made from its Still once that is approved. Approve the Still.",
                ClipState.Queued or ClipState.Generating => "This Clip has not been made yet, so it cannot be approved. Wait for it.",
                ClipState.Approved => "This Clip is already approved.",
                _ => "This Clip was not made, so it cannot be approved. Confirm the estimate again to ask for it.",
            });
        }
        try
        {
            // The approval and the versions that refer to the Clip are kept together, or neither is.
            await database.SaveChangesAsync(cancellationToken);
            if (clip.ProjectId is { } ofProject) database.Storyboards.AddRange(await VersionsReferringAsync(ofProject, now, cancellationToken));
            await database.SaveChangesAsync(cancellationToken);
            if (deciding is not null) await deciding.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClipDecision.Refuse("Someone else decided about this Clip at the same moment. Look at it again.");
        }
        return new ClipDecision((await WithCostsAsync([clip], cancellationToken))[0], null);
    }

    public async Task<Stream?> OpenClipAsync(Guid clipId, CancellationToken cancellationToken)
    {
        var clip = await database.Clips.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clipId, cancellationToken);
        return clip is { HasFootage: true } ? await storage.OpenAsync(clip.StorageKey, cancellationToken) : null;
    }

    // The Project's Clip that was confirmed with a Still of this picture and waits for one to be approved. When a
    // Scene was made longer and confirmed again there are two: the longer is long enough for both, and only it is made.
    private async Task<IReadOnlyList<Clip>> ClipsWaitingForAsync(Still approved, CancellationToken cancellationToken)
    {
        if (approved is not { Technique: Technique.GeneratedFootage, ProjectId: { } projectId }) return [];
        var waiting = await database.Clips
            .Where(c => c.ProjectId == projectId && c.State == ClipState.WaitingForStill && c.Description == approved.Description)
            .ToListAsync(cancellationToken);
        return [.. Longest(waiting.Where(clip => clip.FactIds.SequenceEqual(approved.FactIds)))];
    }

    // Of the Clips that wait for a Still, the one of each picture that will be made: the longest, and of those the newest.
    private static IEnumerable<Clip> Longest(IEnumerable<Clip> waiting) => waiting
        .GroupBy(clip => (clip.Description, Facts: string.Join(",", clip.FactIds)))
        .Select(same => same.OrderByDescending(clip => clip.DurationMs).ThenByDescending(clip => clip.CreatedAt).ThenByDescending(clip => clip.Id).First());

    private static bool IsOf(Still still, Clip clip) =>
        still.Technique == Technique.GeneratedFootage && still.Description == clip.Description && still.FactIds.SequenceEqual(clip.FactIds);

    // A version never changes, so a Still or a Clip that is approved is used by the version after: the next
    // version of each Variant of the Project whose newest version describes the picture it is of.
    private async Task<IReadOnlyList<Storyboard>> VersionsReferringAsync(Guid projectId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var newest = await database.Storyboards.AsNoTracking()
            .Where(s => database.Variants.Any(v => v.Id == s.VariantId && v.ProjectId == projectId))
            .Where(s => s.Version == database.Storyboards.Where(other => other.VariantId == s.VariantId).Max(other => other.Version))
            .ToListAsync(cancellationToken);
        var stills = await database.ApprovedStillsAsync(projectId, cancellationToken);
        var clips = await database.ApprovedClipsAsync(projectId, cancellationToken);
        return
        [
            .. newest
                .Select(from => (From: from, Scenes: from.Scenes.Select(scene => scene.Referring(stills, clips)).ToList()))
                .Where(next => StoryboardEditing.Differ(next.From.Scenes, next.Scenes))
                .Select(next => new Storyboard(
                    Guid.CreateVersion7(), next.From.OrganizationId, next.From.VariantId, next.From.Version + 1, next.From.CreativeTemplate,
                    next.From.TemplateVersion, next.From.Planner, next.From.RenderMode, next.Scenes, now)),
        ];
    }

    // Why a Still in this state cannot be approved or replaced.
    private static string NotToBeDecided(Still still, string decision) => still.State switch
    {
        StillState.Queued or StillState.Generating => $"This Still has not been made yet, so it cannot be {decision}. Wait for it.",
        StillState.Approved => $"This Still is already approved, so it cannot be {decision}.",
        StillState.Rejected => $"Another Still was asked for in place of this one, so it cannot be {decision}.",
        _ => $"This Still was not made, so it cannot be {decision}. Confirm the estimate again to ask for it.",
    };

    private async Task<IReadOnlyList<StillRecord>> WithCostsAsync(IReadOnlyList<Still> stills, CancellationToken cancellationToken)
    {
        var ids = stills.Select(still => (Guid?)still.Id).ToList();
        var costs = await database.GenerationCostRecords.AsNoTracking()
            .Where(cost => ids.Contains(cost.StillId))
            .ToDictionaryAsync(cost => cost.StillId!.Value, cancellationToken);
        return [.. stills.Select(still => new StillRecord(still, costs.GetValueOrDefault(still.Id)))];
    }

    private async Task<IReadOnlyList<ClipRecord>> WithCostsAsync(IReadOnlyList<Clip> clips, CancellationToken cancellationToken)
    {
        var ids = clips.Select(clip => (Guid?)clip.Id).ToList();
        var costs = await database.GenerationCostRecords.AsNoTracking()
            .Where(cost => ids.Contains(cost.ClipId))
            .ToDictionaryAsync(cost => cost.ClipId!.Value, cancellationToken);
        return [.. clips.Select(clip => new ClipRecord(clip, costs.GetValueOrDefault(clip.Id)))];
    }

    private async Task<GenerationEstimate> EstimateAsync(Guid projectId, Storyboard storyboard, CancellationToken cancellationToken)
    {
        var ceiling = await CeilingAsync(cancellationToken);
        var stillsOfProject = await database.Stills.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(cancellationToken);
        var clipsOfProject = await database.Clips.AsNoTracking().Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken);
        var spent = Spent(stillsOfProject, clipsOfProject);
        var options = generation.Value;
        var currency = costs.Value.Currency;

        // A Scene has the Project's Still and Clip of the picture it describes, whichever Variant asked for them first.
        var has = storyboard.Scenes
            .Where(scene => scene.Technique.IsGenerated())
            .Select(scene => (
                Scene: scene,
                Still: stillsOfProject
                    .Where(still => still.IsCurrent && still.Shows(scene))
                    .OrderByDescending(still => still.CreatedAt).ThenByDescending(still => still.Id)
                    .FirstOrDefault(),
                Clip: clipsOfProject
                    .Where(clip => clip.IsCurrent && clip.Shows(scene))
                    .OrderByDescending(clip => clip.CreatedAt).ThenByDescending(clip => clip.Id)
                    .FirstOrDefault()))
            .ToList();
        var stillRecords = await WithCostsAsync([.. has.Where(found => found.Still is not null).Select(found => found.Still!).Distinct()], cancellationToken);
        var clipRecords = await WithCostsAsync([.. has.Where(found => found.Clip is not null).Select(found => found.Clip!).Distinct()], cancellationToken);
        var sceneStills = has
            .Where(found => found.Still is not null)
            .Select(found => new SceneStill(found.Scene.Position, stillRecords.Single(record => record.Still.Id == found.Still!.Id)))
            .ToList();
        var sceneClips = has
            .Where(found => found.Clip is not null)
            .Select(found => new SceneClip(found.Scene.Position, clipRecords.Single(record => record.Clip.Id == found.Clip!.Id)))
            .ToList();

        GenerationEstimate Unavailable(string reason) => new(
            reason, [], [], 0m, 0m, currency, spent, ceiling, options.ImageProvider, options.VideoProvider, sceneStills, sceneClips, AnotherStillAmount: null);

        if (!options.CanGenerateStills) return Unavailable(NoImageProvider);
        var needClip = has.Where(found => found.Scene.Technique == Technique.GeneratedFootage && found.Clip is null).ToList();
        if (needClip.Count > 0 && !options.CanGenerateClips) return Unavailable(NoVideoProvider);

        var stillRate = costs.Value.StillRateOf(options.ImageProvider);
        var stills = has
            // A Still is the backdrop itself, or what a Clip starts from: a Scene whose Clip has started needs none.
            .Where(found => found.Still is null && (found.Clip is null || found.Clip.State == ClipState.WaitingForStill))
            // Two Scenes that describe the same picture share its Still.
            .DistinctBy(found => PictureOf(found.Scene))
            .Select(found => new EstimatedStill(
                found.Scene.Position, found.Scene.Technique, found.Scene.Description ?? "", stillRate, found.Scene.CitedFactIds))
            .ToList();
        var clips = needClip
            // Two Scenes that describe the same footage share its Clip, which is as long as the longer of them.
            .GroupBy(found => PictureOf(found.Scene))
            .Select(same => (First: same.First(), DurationMs: same.Max(found => found.Scene.DurationMs)))
            .Select(asked => new EstimatedClip(
                asked.First.Scene.Position, asked.First.Scene.Description ?? "", asked.DurationMs,
                ProductionCosts.ClipEstimate(costs.Value.ClipRatePerSecondOf(options.VideoProvider), asked.DurationMs),
                asked.First.Scene.CitedFactIds,
                // Made at once from a Still that is already approved. Otherwise it waits for one.
                asked.First.Still is { State: StillState.Approved } approved ? approved.Id : null))
            .ToList();
        // The Clips of these Scenes that wait with no Still on its way, and so are not counted as spent: the Stills asked for here are theirs.
        var resumed = has
            .Where(found => found is { Still: null, Clip.State: ClipState.WaitingForStill })
            .Select(found => found.Clip!).Distinct()
            .Sum(clip => clip.EstimatedAmount);
        return new GenerationEstimate(
            null, stills, clips, stills.Sum(still => still.EstimatedAmount) + clips.Sum(clip => clip.EstimatedAmount), resumed, currency, spent,
            ceiling, options.ImageProvider, options.VideoProvider, sceneStills, sceneClips, AnotherStillAmount: stillRate);
    }

    // What makes two Scenes ask for the same picture: its Technique, what it shows and the Facts it rests on.
    private static (Technique, string?, string) PictureOf(Scene scene) =>
        (scene.Technique, scene.Description, string.Join(",", scene.CitedFactIds));

    private const string RaiseTheCeiling = "An Owner can raise the Organization's ceiling.";

    // The ceiling is the Organization's, and applies to each of its Projects.
    private Task<decimal> CeilingAsync(CancellationToken cancellationToken) =>
        database.Organizations.Where(o => o.Id == caller.OrganizationId).Select(o => o.GenerationCeiling).SingleAsync(cancellationToken);

    private async Task<decimal> SpentAsync(Guid projectId, CancellationToken cancellationToken) =>
        Spent(
            await database.Stills.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(cancellationToken),
            await database.Clips.AsNoTracking().Where(c => c.ProjectId == projectId).ToListAsync(cancellationToken));

    // What is on its way counts as spent already: it was confirmed, and will be charged for. A Clip that waits
    // for a Still is on its way only while a Still of its picture is: when that Still was not made, or a longer
    // Clip of the same picture waits beside it, nothing will be charged for it, and it is not counted.
    private static decimal Spent(IReadOnlyList<Still> stills, IReadOnlyList<Clip> clips) =>
        stills.Where(still => still.CountsAsSpent).Sum(still => still.EstimatedAmount)
        + clips.Where(clip => clip.State != ClipState.WaitingForStill && clip.CountsAsSpent).Sum(clip => clip.EstimatedAmount)
        + Longest(clips.Where(clip => clip.State == ClipState.WaitingForStill))
            .Where(clip => stills.Any(still => still.IsCurrent && IsOf(still, clip)))
            .Sum(clip => clip.EstimatedAmount);

    private async Task<(IReadOnlyList<StillRecord> Stills, IReadOnlyList<ClipRecord> Clips)> FindByKeyAsync(
        Guid projectId, string idempotencyKey, CancellationToken cancellationToken) => (
        await WithCostsAsync(
            await database.Stills.AsNoTracking()
                .Where(s => s.ProjectId == projectId && s.IdempotencyKey == idempotencyKey)
                .OrderBy(s => s.Ordinal)
                .ToListAsync(cancellationToken),
            cancellationToken),
        await WithCostsAsync(
            await database.Clips.AsNoTracking()
                .Where(c => c.ProjectId == projectId && c.IdempotencyKey == idempotencyKey)
                .OrderBy(c => c.Ordinal)
                .ToListAsync(cancellationToken),
            cancellationToken));

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
