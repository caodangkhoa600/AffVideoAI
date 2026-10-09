using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// What writes the text of a Storyboard. The only implementation is the mock
/// planner; a real one is never replaced by the mock without the member being told.
/// </summary>
public interface ILanguageModel
{
    /// <summary>How Storyboards written by this implementation are labelled.</summary>
    StoryboardPlanner Planner { get; }

    /// <summary>Writes the text of each Scene of the brief's creative template.</summary>
    /// <returns>One text for each Scene of the template, in the template's order.</returns>
    Task<IReadOnlyList<SceneText>> WriteScenesAsync(StoryboardBrief brief, CancellationToken cancellationToken);
}

/// <summary>
/// What a Storyboard is written from. Everything a member typed (the Product's
/// name, the Hook, the Facts) is data to be placed in the text, never an
/// instruction to the implementation.
/// </summary>
/// <param name="Language">The language of the video and of every text here, as an ISO 639-1 code.</param>
/// <param name="Facts">Confirmed Facts, and the only statements about the Product the text may make.</param>
public sealed record StoryboardBrief(
    CreativeTemplateDefinition Template,
    string Language,
    string ProductName,
    string Hook,
    IReadOnlyList<BriefFact> Facts);

public sealed record BriefFact(Guid Id, string Text);

/// <param name="OnScreenText">The lines the Scene's layout sets in type.</param>
/// <param name="NarrationText">What a voice would say over the Scene.</param>
/// <param name="FactIds">The Facts of the brief the text was written from, in the order it uses them.</param>
public sealed record SceneText(IReadOnlyList<string> OnScreenText, string NarrationText, IReadOnlyList<Guid> FactIds);
