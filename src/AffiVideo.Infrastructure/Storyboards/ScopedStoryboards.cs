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
// ever added: nothing here changes one, and an edit adds the next.
internal sealed class ScopedStoryboards(
    AffiVideoDbContext database, Caller caller, ILanguageModel languageModel, TimeProvider clock) : IStoryboards
{
    // How often a version number taken by a Storyboard made at the same moment is given up for the next.
    private const int MaxAttempts = 3;

    public async Task<StoryboardGeneration?> GenerateAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken)
    {
        var variant = await FindVariantAsync(projectId, variantId, cancellationToken);
        var project = await database.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (variant is null || project is null) return null;

        if (CreativeTemplates.Find(variant.CreativeTemplate) is not { } template)
        {
            return StoryboardGeneration.Refuse(
                "Only Product Showcase can be planned so far. Add a Variant with that creative template to generate a Storyboard.");
        }

        var material = await MaterialAsync(project, cancellationToken);
        var confirmed = material.Confirmed;
        if (confirmed.Count == 0) return StoryboardGeneration.Refuse(NoConfirmedFact);
        var photos = material.Photos;
        if (photos.Count == 0) return StoryboardGeneration.Refuse(NoUsablePhoto);

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
        if (TooLong(template, material.Product.Name, variant.Hook, facts) is { } reason) return StoryboardGeneration.Refuse(reason);

        var texts = await WriteAsync(template, project, material.Product, variant, facts, cancellationToken);
        const RenderMode renderMode = RenderMode.ProductLock;
        var durations = template.SceneDurations(project.TargetDurationSeconds);
        var scenes = template.Scenes
            .Select((slot, index) => Written(index + 1, slot, durations[index], renderMode, texts[index], facts, photos))
            .ToList();

        return await CheckedAndSavedAsync(
            variant, project, template, variant.CreativeTemplate, template.Version, languageModel.Planner, renderMode, scenes, material,
            cancellationToken);
    }

    public async Task<StoryboardGeneration?> EditAsync(
        Guid projectId, Guid variantId, int version, IReadOnlyList<SceneChange> changes, CancellationToken cancellationToken)
    {
        if (await FindVersionAsync(projectId, variantId, version, cancellationToken) is not var (variant, project, from)) return null;
        if (CreativeTemplates.Find(from.CreativeTemplate) is not { } template) return StoryboardGeneration.Refuse(NoTemplate);

        if (StoryboardEditing.Mismatch(from.Scenes, changes) is { } mismatch) return StoryboardGeneration.Refuse(mismatch);
        var scenes = StoryboardEditing.Apply(from.Scenes, changes);
        if (!StoryboardEditing.Differ(from.Scenes, scenes))
        {
            return StoryboardGeneration.Refuse("This edit changes nothing, so no version was made.");
        }

        return await CheckedAndSavedAsync(
            variant, project, template, from.CreativeTemplate, from.TemplateVersion, from.Planner, from.RenderMode, scenes,
            await MaterialAsync(project, cancellationToken), cancellationToken);
    }

    public async Task<StoryboardGeneration?> RegenerateSceneAsync(
        Guid projectId, Guid variantId, int version, int position, CancellationToken cancellationToken)
    {
        if (await FindVersionAsync(projectId, variantId, version, cancellationToken) is not var (variant, project, from)) return null;
        if (from.Scenes.SingleOrDefault(scene => scene.Position == position) is not { } replaced) return null;
        if (CreativeTemplates.Find(from.CreativeTemplate) is not { } template) return StoryboardGeneration.Refuse(NoTemplate);

        var material = await MaterialAsync(project, cancellationToken);
        if (material.Photos.Count == 0) return StoryboardGeneration.Refuse(NoUsablePhoto);

        var slot = template.Scenes.Select((scene, index) => (Scene: scene, Index: index)).First(found => found.Scene.Layout == replaced.Layout);
        // The Scene keeps the time it has, so the Facts it shows are those that can be read in that time.
        var facts = material.Confirmed.Take(template.MaxFacts).ToList();
        if (replaced.Layout == SceneLayout.Facts)
        {
            if (facts.Count == 0) return StoryboardGeneration.Refuse(NoConfirmedFact);
            var shown = template.FactsShownIn(material.Confirmed.Select(fact => fact.Text).ToArray(), replaced.DurationMs);
            if (shown == 0)
            {
                return StoryboardGeneration.Refuse(
                    $"A Fact has too many words to be read in the {replaced.DurationMs / 1000m:0.#} seconds Scene {position} lasts: at most " +
                    $"{template.MaxFactWordsIn(replaced.DurationMs, factCount: 1)} are on screen long enough. " +
                    $"Give the Scene more time, or replace the Fact with a shorter one: \"{facts[0].Text}\"");
            }
            facts = [.. facts.Take(shown)];
        }

        var texts = await WriteAsync(template, project, material.Product, variant, facts, cancellationToken);
        var written = Written(position, slot.Scene, replaced.DurationMs, from.RenderMode, texts[slot.Index], facts, material.Photos);
        // Every other Scene goes into the next version exactly as it is in this one.
        var scenes = from.Scenes
            .Select(scene => scene.Position == position ? written : scene.Edited(scene.Position, new SceneChange(scene.Position)))
            .ToList();

        return await CheckedAndSavedAsync(
            variant, project, template, from.CreativeTemplate, from.TemplateVersion, from.Planner, from.RenderMode, scenes, material,
            cancellationToken);
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

    private const string NoConfirmedFact =
        "This Product has no Confirmed Fact in the language of the video. Confirm at least one, then generate again.";

    private const string NoTemplate = "This Storyboard's creative template can no longer be planned, so it cannot be edited.";

    private static readonly string NoUsablePhoto =
        $"This Product has no usable photo. Upload a photo at least {ProductAsset.MinVideoPhotoSide} pixels on each side, then generate again.";

    // The Variant has to be this Project's: an identifier from one Project does not work under another.
    private Task<Variant?> FindVariantAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        database.Variants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken);

    private async Task<(Variant Variant, Project Project, Storyboard Storyboard)?> FindVersionAsync(
        Guid projectId, Guid variantId, int version, CancellationToken cancellationToken)
    {
        var variant = await FindVariantAsync(projectId, variantId, cancellationToken);
        var project = await database.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (variant is null || project is null) return null;
        var storyboard = await database.Storyboards.AsNoTracking()
            .SingleOrDefaultAsync(s => s.VariantId == variantId && s.Version == version, cancellationToken);
        return storyboard is null ? null : (variant, project, storyboard);
    }

    /// <summary>What a Storyboard of the Project is made from, as it is now.</summary>
    /// <param name="Confirmed">The Product's Confirmed Facts in the language of the video, oldest first.</param>
    /// <param name="Assets">Everything the Product has, oldest first.</param>
    /// <param name="Photos">Those of the assets a video can show.</param>
    private sealed record Material(
        Product Product, IReadOnlyList<Fact> Confirmed, IReadOnlyList<ProductAsset> Assets, IReadOnlyList<ProductAsset> Photos);

    private async Task<Material> MaterialAsync(Project project, CancellationToken cancellationToken)
    {
        var product = await database.Products.AsNoTracking().SingleAsync(p => p.Id == project.ProductId, cancellationToken);
        var confirmed = await database.Facts.AsNoTracking()
            .Where(f => f.ProductId == product.Id && f.State == FactState.Confirmed && f.Language == project.Language)
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);
        var assets = await database.ProductAssets.AsNoTracking()
            .Where(a => a.ProductId == product.Id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return new Material(product, confirmed, assets, assets.Where(a => a.IsUsableInVideo).ToList());
    }

    /// <returns>One text for each Scene of the template, in the template's order.</returns>
    private async Task<IReadOnlyList<SceneText>> WriteAsync(
        CreativeTemplateDefinition template, Project project, Product product, Variant variant, IReadOnlyList<Fact> facts,
        CancellationToken cancellationToken)
    {
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
        return texts;
    }

    // One Scene as the planner wrote it and the planning engine produces it.
    private static Scene Written(
        int position, SceneSlot slot, int durationMs, RenderMode renderMode, SceneText text,
        IReadOnlyList<Fact> facts, IReadOnlyList<ProductAsset> photos) =>
        new(
            position, slot.Layout, PlanningEngine.TechniqueFor(slot, photos.Count, durationMs, renderMode), durationMs,
            text.OnScreenText, text.NarrationText,
            [(slot.Photo == ScenePhoto.Last ? photos[^1] : photos[0]).Id],
            // The text is copied from the Fact as it is now. A Fact the brief did not offer has no
            // text to copy, and validation then refuses the Storyboard for citing it.
            text.FactIds.Select((factId, index) => new SceneFact(
                index + 1, factId, facts.FirstOrDefault(fact => fact.Id == factId)?.Text ?? "")));

    // Whatever wrote the text, and whoever changed it, nothing is kept that breaks the duration, shows
    // what the Product does not have, cites a Fact that is not Confirmed or does not fit its layout.
    private async Task<StoryboardGeneration?> CheckedAndSavedAsync(
        Variant variant, Project project, CreativeTemplateDefinition template,
        CreativeTemplate creativeTemplate, int templateVersion, StoryboardPlanner planner, RenderMode renderMode,
        IReadOnlyList<Scene> scenes, Material material, CancellationToken cancellationToken)
    {
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Storyboard is made for the caller's Organization, and there is no caller.");

        var confirmed = material.Confirmed.Select(fact => fact.Id).ToHashSet();
        var problems = StoryboardRules.Problems(
            scenes, project.TargetDurationSeconds, renderMode, material.Assets.Select(asset => asset.Id).ToHashSet(), confirmed).ToList();
        if (scenes.Any(scene => scene.Facts.Any(fact => !confirmed.Contains(fact.FactId))))
        {
            problems.Add("Regenerate a Scene that uses such a Fact, or change its text yourself, and it no longer rests on it.");
        }
        var unusable = material.Assets.Where(asset => !asset.IsUsableInVideo).Select(asset => asset.Id).ToHashSet();
        problems.AddRange(scenes
            .Where(scene => scene.AssetIds.Any(unusable.Contains))
            .Select(scene =>
                $"Scene {scene.Position} shows an image that cannot be shown in a video: " +
                $"a Scene shows a photo, not the logo, of at least {ProductAsset.MinVideoPhotoSide} pixels on each side."));
        problems.AddRange(StoryboardRules.LayoutProblems(scenes, template));
        if (problems.Count > 0) return StoryboardGeneration.Refuse(string.Join(" ", problems));

        for (var attempt = 1; ; attempt++)
        {
            var latest = await database.Storyboards.Where(s => s.VariantId == variant.Id).MaxAsync(s => (int?)s.Version, cancellationToken);
            var storyboard = new Storyboard(
                Guid.CreateVersion7(), organizationId, variant.Id, (latest ?? 0) + 1,
                creativeTemplate, templateVersion, planner, renderMode, scenes, clock.GetUtcNow());
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
}
