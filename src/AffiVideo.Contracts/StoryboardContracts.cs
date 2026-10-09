using System.ComponentModel.DataAnnotations;
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
/// <param name="Facts">The Facts the Scene's text was written from, in the order it uses them. None when a person wrote the text.</param>
/// <param name="ManuallyEdited">Whether a person changed the Scene's text. Such text is the Organization's own and is not checked against Facts.</param>
public sealed record SceneResponse(
    int Position,
    SceneLayout Layout,
    Technique Technique,
    int DurationMs,
    IReadOnlyList<string> OnScreenText,
    string NarrationText,
    IReadOnlyList<Guid> AssetIds,
    IReadOnlyList<SceneFactResponse> Facts,
    bool ManuallyEdited);

/// <param name="Text">The Fact's text as the Scene used it, kept with the Scene.</param>
public sealed record SceneFactResponse(Guid FactId, string Text);

/// <summary>An edit of one Storyboard version, which makes the Variant's next version and leaves this one as it is.</summary>
/// <param name="Scenes">Every Scene of the version being edited, once, in the order the next version plays them.</param>
public sealed record StoryboardEditRequest(
    [Required, MinLength(1, ErrorMessage = "Name every Scene of the version being edited, in the order they are to play.")]
    IReadOnlyList<SceneEditRequest> Scenes);

/// <summary>What to change about one Scene. Whatever is left out stays as it is.</summary>
/// <param name="Position">Of the Scene in the version being edited.</param>
/// <param name="OnScreenText">The lines its layout sets in type. Changing them marks the Scene Manually Edited.</param>
/// <param name="NarrationText">What a voice would say over the Scene. Changing it marks the Scene Manually Edited.</param>
/// <param name="AssetId">The photo of the Product the Scene shows.</param>
/// <param name="DurationMs">In milliseconds, a whole number of tenths of a second. The Scenes still have to sum to the target duration.</param>
public sealed record SceneEditRequest(
    int Position,
    IReadOnlyList<string>? OnScreenText = null,
    string? NarrationText = null,
    Guid? AssetId = null,
    int? DurationMs = null);
