using AffiVideo.Application.Providers;
using AffiVideo.Domain;

namespace AffiVideo.Infrastructure.Planning;

// The mock planner. It writes nothing of its own: each Scene's text is a fixed
// Vietnamese sentence pattern of the creative template's layout with the
// Product's name, the Hook or a Fact placed in it whole. No pattern says anything
// about the Product: only the Facts do. What a member typed is only ever placed, never read
// for meaning, so no text can change what is written around it, and the same
// brief always gives the same texts.
internal sealed class MockLanguageModel : ILanguageModel
{
    private const string SolutionLabel = "Giải pháp";

    public StoryboardPlanner Planner => StoryboardPlanner.Mock;

    public Task<IReadOnlyList<SceneText>> WriteScenesAsync(StoryboardBrief brief, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SceneText>>(brief.Template.Scenes.Select(scene => Write(scene.Layout, brief)).ToArray());

    private static SceneText Write(SceneLayout layout, StoryboardBrief brief) => layout switch
    {
        SceneLayout.Hook => new SceneText([brief.Hook], brief.Hook, []),
        SceneLayout.Reveal => new SceneText([brief.ProductName], $"Đây là {brief.ProductName}.", []),
        SceneLayout.Solution => new SceneText([SolutionLabel, brief.ProductName], $"{SolutionLabel}: {Sentence(brief.ProductName)}", []),
        SceneLayout.Facts => new SceneText(
            brief.Facts.Select(fact => fact.Text).ToArray(),
            string.Join(" ", brief.Facts.Select(fact => Sentence(fact.Text))),
            brief.Facts.Select(fact => fact.Id).ToArray()),
        SceneLayout.Closing => Closing(brief),
        _ => throw new InvalidOperationException($"The mock planner has no sentence pattern for the {layout} layout."),
    };

    // Each creative template closes on a call to action of its own: the line set in type, and how it is said.
    private static SceneText Closing(StoryboardBrief brief)
    {
        var (onScreen, said) = brief.Template.Template switch
        {
            CreativeTemplate.LuxuryCinematic => ("Khám phá ngay", "Khám phá ngay hôm nay."),
            CreativeTemplate.ProductShowcase => ("Xem chi tiết sản phẩm", "Xem chi tiết sản phẩm ngay hôm nay."),
            CreativeTemplate.ProblemSolution => ("Xem giải pháp ngay", "Xem giải pháp ngay hôm nay."),
            var other => throw new InvalidOperationException($"The mock planner has no call to action for {other}."),
        };
        return new SceneText([brief.ProductName, onScreen], $"{Sentence(brief.ProductName)} {said}", []);
    }

    // The text as a sentence of its own: closed with a full stop unless it closes itself.
    private static string Sentence(string text) => text[^1] is '.' or '!' or '?' or '…' ? text : $"{text}.";
}
