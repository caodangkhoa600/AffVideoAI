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
        IEnumerable<string> onScreenText, string narrationText, IEnumerable<Guid> assetIds, IEnumerable<SceneFact> facts,
        bool manuallyEdited = false)
    {
        Position = position;
        Layout = layout;
        Technique = technique;
        DurationMs = durationMs;
        OnScreenText = [.. onScreenText];
        NarrationText = narrationText;
        AssetIds = [.. assetIds];
        _facts.AddRange(facts);
        ManuallyEdited = manuallyEdited;
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

    /// <summary>
    /// Whether a person changed the Scene's text. Such text is the Organization's
    /// own: it cites no Facts and is not checked against them. The mark stays
    /// until the Scene is regenerated.
    /// </summary>
    public bool ManuallyEdited { get; private set; }

    /// <summary>
    /// The Scene as the next version has it, at this position and with this change
    /// made. A version never changes, so this is another Scene and not this one.
    /// </summary>
    public Scene Edited(int position, SceneChange change)
    {
        string[] onScreenText = change.OnScreenText is { } lines ? [.. lines.Select(line => (line ?? "").Trim())] : OnScreenText;
        var narrationText = change.NarrationText?.Trim() ?? NarrationText;
        var textChanged = !onScreenText.SequenceEqual(OnScreenText) || narrationText != NarrationText;
        var manuallyEdited = ManuallyEdited || textChanged;
        return new Scene(
            position, Layout, Technique, change.DurationMs ?? DurationMs, onScreenText, narrationText,
            change.AssetId is { } asset ? [asset] : AssetIds,
            // Text a person wrote is no longer traceable to Facts.
            manuallyEdited ? [] : Facts.Select(fact => new SceneFact(fact.Position, fact.FactId, fact.Text)),
            manuallyEdited);
    }
}

/// <summary>
/// What a member asks of one Scene of the Storyboard version being edited.
/// Whatever is left out stays as it is.
/// </summary>
/// <param name="Position">Of the Scene in the version being edited.</param>
/// <param name="OnScreenText">The lines its layout sets in type.</param>
/// <param name="AssetId">The photo it shows.</param>
/// <param name="DurationMs">In milliseconds.</param>
public sealed record SceneChange(
    int Position, IReadOnlyList<string>? OnScreenText = null, string? NarrationText = null, Guid? AssetId = null, int? DurationMs = null);

/// <summary>An edit of a Storyboard version: its Scenes, each named once, in the order the next version plays them.</summary>
public static class StoryboardEditing
{
    /// <summary>Why these changes are not an edit of these Scenes, in words for the member. Null when they are.</summary>
    public static string? Mismatch(IReadOnlyList<Scene> scenes, IReadOnlyList<SceneChange> changes) =>
        changes.Select(change => change.Position).Order().SequenceEqual(scenes.Select(scene => scene.Position).Order())
            ? null
            : "An edit names every Scene of the version once, in the order they are to play. This version has Scenes " +
              $"{string.Join(", ", scenes.Select(scene => scene.Position))}.";

    /// <summary>The Scenes of the next version: in the order of the changes, counted from 1 again, each with its change made.</summary>
    public static IReadOnlyList<Scene> Apply(IReadOnlyList<Scene> scenes, IReadOnlyList<SceneChange> changes) =>
        changes.Select((change, index) => scenes.Single(scene => scene.Position == change.Position).Edited(index + 1, change)).ToArray();

    /// <summary>Whether anything a member could see differs between the two lists of Scenes.</summary>
    public static bool Differ(IReadOnlyList<Scene> from, IReadOnlyList<Scene> to) =>
        from.Count != to.Count || from.Zip(to).Any(pair => !Same(pair.First, pair.Second));

    private static bool Same(Scene a, Scene b) =>
        a.Position == b.Position && a.Layout == b.Layout && a.Technique == b.Technique && a.DurationMs == b.DurationMs
        && a.OnScreenText.SequenceEqual(b.OnScreenText) && a.NarrationText == b.NarrationText
        && a.AssetIds.SequenceEqual(b.AssetIds) && a.ManuallyEdited == b.ManuallyEdited
        && a.Facts.Select(fact => fact.FactId).SequenceEqual(b.Facts.Select(fact => fact.FactId));
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
    /// <summary>The most characters a Scene's narration text has.</summary>
    public const int NarrationMaxLength = 500;

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

    /// <summary>
    /// Everything about these Scenes that the creative template's layouts do not
    /// hold, in words for the member, whatever wrote the text: the Hook's Scene
    /// not opening the video, a Scene shorter than its layout needs, more or fewer
    /// lines than the layout sets, a line too long for it, Facts given too little
    /// time to be read, or narration too long. Empty when everything fits.
    /// </summary>
    public static IReadOnlyList<string> LayoutProblems(IReadOnlyList<Scene> scenes, CreativeTemplateDefinition template)
    {
        var problems = new List<string>();
        if (scenes.Count > 0 && scenes[0].Layout != SceneLayout.Hook && scenes.Any(scene => scene.Layout == SceneLayout.Hook))
        {
            problems.Add("The Hook opens the video: its Scene has to stay first, so that the Hook is on screen within the first two seconds.");
        }

        foreach (var scene in scenes)
        {
            var name = $"Scene {scene.Position}";
            if (template.Scenes.FirstOrDefault(slot => slot.Layout == scene.Layout) is { } slot
                && scene.DurationMs > 0 && scene.DurationMs < slot.MinDurationMs)
            {
                problems.Add(
                    $"{name} lasts {Seconds(scene.DurationMs)} seconds, and a {scene.Layout} Scene needs at least " +
                    $"{Seconds(slot.MinDurationMs)} for everything in it to arrive.");
            }
            if (scene.NarrationText.Length > NarrationMaxLength)
            {
                problems.Add($"{name}'s narration has {scene.NarrationText.Length} characters, and at most {NarrationMaxLength} can be said in a Scene.");
            }
            problems.AddRange(TextProblems(scene, name, template));
        }
        return problems;
    }

    // What each line of a layout is, and so how much it holds, is SceneLayout's own description of it.
    private static IEnumerable<string> TextProblems(Scene scene, string name, CreativeTemplateDefinition template)
    {
        var lines = scene.OnScreenText;
        var (fewest, most) = scene.Layout switch
        {
            SceneLayout.Facts => (1, template.MaxFacts),
            SceneLayout.Closing or SceneLayout.Solution => (2, 2),
            _ => (1, 1),
        };
        if (lines.Length < fewest || lines.Length > most)
        {
            var sets = fewest == most ? $"exactly {fewest}" : $"from {fewest} to {most}";
            return [$"{name} has {lines.Length} {(lines.Length == 1 ? "line" : "lines")} of on-screen text, and its {scene.Layout} layout sets {sets}."];
        }
        if (lines.Any(string.IsNullOrWhiteSpace)) return [$"{name}'s on-screen text is empty. Every line its layout sets has to say something."];

        switch (scene.Layout)
        {
            case SceneLayout.Hook:
                return Exceeded(template.HookLimit, $"{name}'s text", lines[0]);
            case SceneLayout.Reveal:
                return Exceeded(template.NameLimit, $"{name}'s text", lines[0]);
            case SceneLayout.Closing:
                return
                [
                    .. Exceeded(template.NameLimit, $"{name}'s name", lines[0]),
                    .. Exceeded(template.CallToActionLimit, $"{name}'s call to action", lines[1]),
                ];
            case SceneLayout.Solution:
                return
                [
                    .. template.LabelLimit is { } label ? Exceeded(label, $"{name}'s label", lines[0]) : [],
                    .. Exceeded(template.NameLimit, $"{name}'s name", lines[1]),
                ];
            default:
                var problems = lines.SelectMany((line, index) => Exceeded(template.FactLimit, $"{name}'s line {index + 1}", line)).ToList();
                var readable = template.MaxFactWordsIn(scene.DurationMs, lines.Length);
                var longest = lines.Max(line => TextLimit.Words(line).Length);
                if (longest > readable)
                {
                    problems.Add(
                        $"{name} gives its text too little time to be read: a line has {longest} words, and at most {readable} are on screen long enough " +
                        $"when {(lines.Length == 1 ? "one line has" : $"{lines.Length} lines share")} {Seconds(scene.DurationMs)} seconds. " +
                        "Give the Scene more time, or shorten or remove a line.");
                }
                return problems;
        }

        static IEnumerable<string> Exceeded(TextLimit limit, string subject, string text) =>
            limit.Exceeded(subject, text) is { } reason ? [reason] : [];
    }

    /// <summary>
    /// Everything that stops a Storyboard version being rendered as it stands now, in
    /// words for the member. A version never changes, but what it was planned from
    /// can: a photo it shows may since have been removed. Empty when it can be rendered.
    /// </summary>
    /// <param name="assets">Those of the assets the Scenes show that the Product still has.</param>
    public static IReadOnlyList<string> RenderProblems(Storyboard storyboard, IReadOnlyCollection<ProductAsset> assets)
    {
        if (CreativeTemplates.Find(storyboard.CreativeTemplate) is not { } template || storyboard.RenderMode != RenderMode.ProductLock)
        {
            return ["Only a Storyboard in Product Lock can be rendered so far."];
        }
        if (storyboard.Scenes.Count == 0) return ["The Storyboard has no Scenes."];

        var problems = new List<string>();
        var removed = false;
        foreach (var scene in storyboard.Scenes)
        {
            var name = $"Scene {scene.Position}";
            if (scene.DurationMs <= 0 || scene.DurationMs % SceneDurations.StepMilliseconds != 0)
            {
                problems.Add($"{name} lasts {Seconds(scene.DurationMs)} seconds. A Scene lasts a whole number of tenths of a second, and at least one.");
            }
            if (!storyboard.RenderMode.Allows(scene.Technique))
            {
                problems.Add($"{name} is planned as {scene.Technique}, which {storyboard.RenderMode} does not allow.");
            }
            if (template.Scenes.All(slot => slot.Layout != scene.Layout))
            {
                problems.Add($"{name} has the {scene.Layout} layout, which its creative template does not draw.");
            }

            if (scene.AssetIds.Length != 1)
            {
                problems.Add($"{name} shows {scene.AssetIds.Length} photos. A Scene of this creative template shows exactly one.");
            }
            else if (assets.FirstOrDefault(asset => asset.Id == scene.AssetIds[0]) is not { } photo)
            {
                problems.Add($"{name} shows a photo that has since been removed from the Product.");
                removed = true;
            }
            else if (!photo.IsUsableInVideo)
            {
                problems.Add($"{name} shows an image that cannot be shown in a video.");
            }
        }
        if (removed) problems.Add("Generate the Storyboard again, then render that version.");
        return problems;
    }

    private static string Seconds(int milliseconds) => (milliseconds / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
}
