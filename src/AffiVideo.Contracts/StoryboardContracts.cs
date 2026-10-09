using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>One version of a Variant's Storyboard. Nothing about a version ever changes.</summary>
/// <param name="Version">Counted from 1 within the Variant.</param>
/// <param name="TemplateVersion">The version of the creative template the Scenes were planned with.</param>
/// <param name="Planner">What wrote the text. <c>Mock</c> is fixed sentence patterns filled with Confirmed Facts, and no AI.</param>
/// <param name="Scenes">In the order they play. Their durations sum to the Project's target duration.</param>
public sealed record StoryboardResponse(
    Guid Id,
    Guid VariantId,
    int Version,
    CreativeTemplate CreativeTemplate,
    int TemplateVersion,
    StoryboardPlanner Planner,
    RenderMode RenderMode,
    IReadOnlyList<SceneResponse> Scenes,
    DateTimeOffset CreatedAt);

/// <param name="Position">Counted from 1 within the Storyboard.</param>
/// <param name="Layout">Which layout of the creative template draws the Scene.</param>
/// <param name="DurationMs">In milliseconds, always a whole number of tenths of a second.</param>
/// <param name="OnScreenText">The lines the layout sets in type. What each line is depends on the layout.</param>
/// <param name="NarrationText">What a voice would say over the Scene.</param>
/// <param name="AssetIds">The Product's assets the Scene shows.</param>
/// <param name="Facts">The Facts the Scene's text was written from, in the order it uses them.</param>
public sealed record SceneResponse(
    int Position,
    SceneLayout Layout,
    Technique Technique,
    int DurationMs,
    IReadOnlyList<string> OnScreenText,
    string NarrationText,
    IReadOnlyList<Guid> AssetIds,
    IReadOnlyList<SceneFactResponse> Facts);

/// <param name="Text">The Fact's text as the Scene used it, kept with the Scene.</param>
public sealed record SceneFactResponse(Guid FactId, string Text);
