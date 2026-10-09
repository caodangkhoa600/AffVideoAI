using AffiVideo.Application.Providers;
using AffiVideo.Domain;

namespace AffiVideo.Infrastructure.Planning;

// The mock planner. It writes nothing of its own: each Scene's text is a fixed
// Vietnamese sentence pattern of the layout with the Product's name, the Hook or
// a Fact placed in it whole. What a member typed is only ever placed, never read
// for meaning, so no text can change what is written around it, and the same
// brief always gives the same texts.
internal sealed class MockLanguageModel : ILanguageModel
{
    private const string CallToAction = "Xem chi tiết sản phẩm";

    public StoryboardPlanner Planner => StoryboardPlanner.Mock;

    public Task<IReadOnlyList<SceneText>> WriteScenesAsync(StoryboardBrief brief, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SceneText>>(brief.Template.Scenes.Select(scene => Write(scene.Layout, brief)).ToArray());

    private static SceneText Write(SceneLayout layout, StoryboardBrief brief) => layout switch
    {
        SceneLayout.Hook => new SceneText([brief.Hook], brief.Hook, []),
        SceneLayout.Reveal => new SceneText([brief.ProductName], $"Đây là {brief.ProductName}.", []),
        SceneLayout.Facts => new SceneText(
            brief.Facts.Select(fact => fact.Text).ToArray(),
            string.Join(" ", brief.Facts.Select(fact => Sentence(fact.Text))),
            brief.Facts.Select(fact => fact.Id).ToArray()),
        SceneLayout.Closing => new SceneText(
            [brief.ProductName, CallToAction], $"{Sentence(brief.ProductName)} {CallToAction} ngay hôm nay.", []),
        _ => throw new InvalidOperationException($"The mock planner has no sentence pattern for the {layout} layout."),
    };

    // The text as a sentence of its own: closed with a full stop unless it closes itself.
    private static string Sentence(string text) => text[^1] is '.' or '!' or '?' or '…' ? text : $"{text}.";
}
