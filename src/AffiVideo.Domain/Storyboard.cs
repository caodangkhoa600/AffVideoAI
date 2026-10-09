using System.Globalization;

namespace AffiVideo.Domain;

/// <summary>
/// One version of the ordered Scenes of a Variant. Nothing about a version ever
/// changes: an edit makes the next version. It holds everything the renderer
/// needs apart from the Product's assets themselves.
/// </summary>
public sealed class Storyboard : IOwnedByOrganization
{
    private readonly List<Scene> _scenes = [];

    // For the data-access layer, which fills the properties itself.
    private Storyboard()
    {
    }

    public Storyboard(
        Guid id, Guid organizationId, Guid variantId, int version,
        CreativeTemplate creativeTemplate, int templateVersion, StoryboardPlanner planner, RenderMode renderMode,
        IEnumerable<Scene> scenes, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        VariantId = variantId;
        Version = version;
        CreativeTemplate = creativeTemplate;
        TemplateVersion = templateVersion;
        Planner = planner;
        RenderMode = renderMode;
        _scenes.AddRange(scenes);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid VariantId { get; private set; }

    /// <summary>Counted from 1 within the Variant.</summary>
    public int Version { get; private set; }

    public CreativeTemplate CreativeTemplate { get; private set; }

    /// <summary>The <see cref="CreativeTemplateDefinition.Version"/> the Scenes were planned with.</summary>
    public int TemplateVersion { get; private set; }

    /// <summary>What wrote the text.</summary>
    public StoryboardPlanner Planner { get; private set; }

    public RenderMode RenderMode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>In the order they play.</summary>
    public IReadOnlyList<Scene> Scenes => [.. _scenes.OrderBy(scene => scene.Position)];
}

/// <summary>What wrote a Storyboard's text.</summary>
public enum StoryboardPlanner
{
    /// <summary>The mock planner: fixed sentence patterns filled with Confirmed Facts, and no AI.</summary>
    Mock,
}

/// <summary>One timed segment of a Storyboard, with its text, source assets and Technique.</summary>
public sealed class Scene
{
    private readonly List<SceneFact> _facts = [];

    // For the data-access layer, which fills the properties itself.
    private Scene()
    {
    }

    public Scene(
        int position, SceneLayout layout, Technique technique, int durationMs,
        IEnumerable<string> onScreenText, string narrationText, IEnumerable<Guid> assetIds, IEnumerable<SceneFact> facts)
    {
        Position = position;
        Layout = layout;
        Technique = technique;
        DurationMs = durationMs;
        OnScreenText = [.. onScreenText];
        NarrationText = narrationText;
        AssetIds = [.. assetIds];
        _facts.AddRange(facts);
    }

    /// <summary>Counted from 1 within the Storyboard.</summary>
    public int Position { get; private set; }

    /// <summary>Which layout of the creative template draws the Scene.</summary>
    public SceneLayout Layout { get; private set; }

    public Technique Technique { get; private set; }

    /// <summary>In milliseconds, a whole number of <see cref="SceneDurations.StepMilliseconds"/>.</summary>
    public int DurationMs { get; private set; }

    /// <summary>The lines the layout sets in type; what each line is depends on the <see cref="Layout"/>.</summary>
    public string[] OnScreenText { get; private set; } = [];

    /// <summary>What a voice would say over the Scene.</summary>
    public string NarrationText { get; private set; } = "";

    /// <summary>The Product's assets the Scene shows.</summary>
    public Guid[] AssetIds { get; private set; } = [];

    /// <summary>The Facts the Scene's text was written from, in the order it uses them.</summary>
    public IReadOnlyList<SceneFact> Facts => [.. _facts.OrderBy(fact => fact.Position)];
}

/// <summary>
/// A Fact as a Scene used it: which Fact, and a copy of its text, so the record
/// of what the Scene said does not depend on the Fact afterwards.
/// </summary>
public sealed class SceneFact
{
    // For the data-access layer, which fills the properties itself.
    private SceneFact()
    {
    }

    public SceneFact(int position, Guid factId, string text)
    {
        Position = position;
        FactId = factId;
        Text = text;
    }

    /// <summary>Counted from 1 within the Scene.</summary>
    public int Position { get; private set; }

    public Guid FactId { get; private set; }

    public string Text { get; private set; } = "";
}

public static class StoryboardRules
{
    /// <summary>
    /// Everything that makes these Scenes unacceptable as a Storyboard, in words for
    /// the member: Scene durations that do not sum to the target, an asset the
    /// Product does not have, a Fact that is not Confirmed, or a Technique the
    /// Render Mode does not allow. Empty when the Storyboard is acceptable.
    /// </summary>
    public static IReadOnlyList<string> Problems(
        IReadOnlyList<Scene> scenes, int targetDurationSeconds, RenderMode renderMode,
        IReadOnlySet<Guid> productAssetIds, IReadOnlySet<Guid> confirmedFactIds)
    {
        if (scenes.Count == 0) return ["The Storyboard has no Scenes."];

        var problems = new List<string>();
        foreach (var scene in scenes)
        {
            var name = $"Scene {scene.Position}";
            if (scene.DurationMs <= 0 || scene.DurationMs % SceneDurations.StepMilliseconds != 0)
            {
                problems.Add($"{name} lasts {Seconds(scene.DurationMs)} seconds. A Scene lasts a whole number of tenths of a second, and at least one.");
            }
            if (!renderMode.Allows(scene.Technique))
            {
                problems.Add($"{name} is planned as {scene.Technique}, which {renderMode} does not allow.");
            }
            if (scene.AssetIds.Any(asset => !productAssetIds.Contains(asset)))
            {
                problems.Add($"{name} uses an asset the Product does not have.");
            }
            problems.AddRange(scene.Facts
                .Where(fact => !confirmedFactIds.Contains(fact.FactId))
                .Select(fact => $"{name} uses a Fact that is not Confirmed: \"{fact.Text}\"."));
        }

        var total = scenes.Sum(scene => scene.DurationMs);
        if (total != targetDurationSeconds * 1000)
        {
            problems.Add($"The Scenes last {Seconds(total)} seconds in all, and the target duration is {targetDurationSeconds} seconds.");
        }
        return problems;
    }

    private static string Seconds(int milliseconds) => (milliseconds / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
}
