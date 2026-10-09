using AffiVideo.Application;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Storyboards;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Storyboards;

// No method names an Organization: the context's filter leaves only the caller's
// Variants, Products, Facts, assets and Storyboards. A Storyboard version is only
// ever added: nothing here changes one.
internal sealed class ScopedStoryboards(
    AffiVideoDbContext database, Caller caller, ILanguageModel languageModel, TimeProvider clock) : IStoryboards
{
    // How often a version number taken by a Storyboard generated at the same moment is given up for the next.
    private const int MaxAttempts = 3;

    public async Task<StoryboardGeneration?> GenerateAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken)
    {
        var variant = await FindVariantAsync(projectId, variantId, cancellationToken);
        var project = await database.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (variant is null || project is null) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Storyboard is generated for the caller's Organization, and there is no caller.");

        if (CreativeTemplates.Find(variant.CreativeTemplate) is not { } template)
        {
            return StoryboardGeneration.Refuse(
                "Only Product Showcase can be planned so far. Add a Variant with that creative template to generate a Storyboard.");
        }

        var product = await database.Products.AsNoTracking().SingleAsync(p => p.Id == project.ProductId, cancellationToken);
        var confirmed = await database.Facts.AsNoTracking()
            .Where(f => f.ProductId == product.Id && f.State == FactState.Confirmed && f.Language == project.Language)
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);
        if (confirmed.Count == 0)
        {
            return StoryboardGeneration.Refuse(
                "This Product has no Confirmed Fact in the language of the video. Confirm at least one, then generate again.");
        }

        var assets = await database.ProductAssets.AsNoTracking()
            .Where(a => a.ProductId == product.Id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        var photos = assets.Where(a => a.IsUsableInVideo).ToList();
        if (photos.Count == 0)
        {
            return StoryboardGeneration.Refuse(
                $"This Product has no usable photo. Upload a photo at least {ProductAsset.MinVideoPhotoSide} pixels on each side, then generate again.");
        }

        // The oldest Confirmed Facts: as many as the template shows and the duration gives time to read.
        var shown = template.FactsShown(confirmed.Select(fact => fact.Text).ToArray(), project.TargetDurationSeconds);
        if (shown == 0)
        {
            return StoryboardGeneration.Refuse(
                $"A Fact has too many words to be read in a {project.TargetDurationSeconds}-second video: at most " +
                $"{template.MaxFactWords(project.TargetDurationSeconds, factCount: 1)} are on screen long enough. " +
                $"Replace it with a shorter one: \"{confirmed[0].Text}\"");
        }
        var facts = confirmed.Take(shown).ToList();
        if (TooLong(template, product.Name, variant.Hook, facts) is { } reason) return StoryboardGeneration.Refuse(reason);

        var texts = await languageModel.WriteScenesAsync(
            new StoryboardBrief(
                template, project.Language, product.Name, variant.Hook,
                facts.Select(fact => new BriefFact(fact.Id, fact.Text)).ToArray()),
            cancellationToken);
        if (texts.Count != template.Scenes.Count)
        {
            throw new InvalidOperationException(
                $"The {languageModel.Planner} planner wrote {texts.Count} Scenes for a creative template of {template.Scenes.Count}.");
        }

        const RenderMode renderMode = RenderMode.ProductLock;
        var scenes = Scenes(template, project.TargetDurationSeconds, renderMode, texts, facts, photos);
        // Whatever wrote the text, nothing is kept that cites a Fact the brief did not offer or breaks the duration.
        var problems = StoryboardRules.Problems(
            scenes, project.TargetDurationSeconds, renderMode,
            assets.Select(asset => asset.Id).ToHashSet(), facts.Select(fact => fact.Id).ToHashSet());
        if (problems.Count > 0) return StoryboardGeneration.Refuse(string.Join(" ", problems));

        for (var attempt = 1; ; attempt++)
        {
            var latest = await database.Storyboards.Where(s => s.VariantId == variantId).MaxAsync(s => (int?)s.Version, cancellationToken);
            var storyboard = new Storyboard(
                Guid.CreateVersion7(), organizationId, variantId, (latest ?? 0) + 1,
                variant.CreativeTemplate, template.Version, languageModel.Planner, renderMode, scenes, clock.GetUtcNow());
            database.Storyboards.Add(storyboard);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return StoryboardGeneration.Made(storyboard);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // The Project, and the Variant with it, was deleted after it was read.
                return null;
            }
            catch (DbUpdateException exception) when (
                attempt < MaxAttempts && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another Storyboard of this Variant took the version number at the same moment.
                database.ChangeTracker.Clear();
            }
        }
    }

    public async Task<Page<Storyboard>?> ListAsync(Guid projectId, Guid variantId, PageRequest page, CancellationToken cancellationToken)
    {
        if (await FindVariantAsync(projectId, variantId, cancellationToken) is null) return null;

        var all = database.Storyboards.AsNoTracking().Where(s => s.VariantId == variantId);
        var items = await all
            .OrderByDescending(s => s.Version)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<Storyboard>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<Storyboard?> FindAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken) =>
        await FindVariantAsync(projectId, variantId, cancellationToken) is null
            ? null
            : await database.Storyboards.AsNoTracking()
                .SingleOrDefaultAsync(s => s.VariantId == variantId && s.Version == version, cancellationToken);

    // The Variant has to be this Project's: an identifier from one Project does not work under another.
    private Task<Variant?> FindVariantAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        database.Variants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken);

    /// <returns>Why a text does not fit the layout that sets it in type, or null when all of them fit.</returns>
    private static string? TooLong(CreativeTemplateDefinition template, string productName, string hook, IReadOnlyList<Fact> facts)
    {
        if (template.HookLimit.Exceeded("The Hook", hook) is { } hookReason)
        {
            return hookReason + " Duplicate the Variant with a shorter Hook.";
        }
        if (template.NameLimit.Exceeded("The Product's name", productName) is { } nameReason)
        {
            return nameReason + " Give the Product a shorter name.";
        }
        foreach (var fact in facts)
        {
            if (template.FactLimit.Exceeded("A Fact", fact.Text) is { } factReason)
            {
                return factReason + $" Replace it with a shorter one: \"{fact.Text}\"";
            }
        }
        return null;
    }

    private static List<Scene> Scenes(
        CreativeTemplateDefinition template, int targetDurationSeconds, RenderMode renderMode,
        IReadOnlyList<SceneText> texts, IReadOnlyList<Fact> facts, IReadOnlyList<ProductAsset> photos)
    {
        var durations = template.SceneDurations(targetDurationSeconds);
        var techniques = PlanningEngine.AssignTechniques(photos.Count, template, targetDurationSeconds, renderMode);
        return template.Scenes
            .Select((slot, index) => new Scene(
                index + 1, slot.Layout, techniques[index], durations[index],
                texts[index].OnScreenText, texts[index].NarrationText,
                [(slot.Photo == ScenePhoto.Last ? photos[^1] : photos[0]).Id],
                // The text is copied from the Fact as it is now. A Fact the brief did not offer has no
                // text to copy, and validation then refuses the Storyboard for citing it.
                texts[index].FactIds.Select((factId, position) => new SceneFact(
                    position + 1, factId, facts.FirstOrDefault(fact => fact.Id == factId)?.Text ?? ""))))
            .ToList();
    }
}
