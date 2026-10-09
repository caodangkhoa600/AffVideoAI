using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What makes a Storyboard acceptable, as a pure function.</summary>
public sealed class StoryboardValidationTests
{
    private static readonly Guid Photo = Guid.NewGuid();
    private static readonly Guid ConfirmedFact = Guid.NewGuid();

    [Fact]
    public void A_Storyboard_whose_Scenes_fill_the_target_with_the_Products_images_and_Confirmed_Facts_is_accepted()
    {
        var problems = Problems([
            NewScene(1, durationMs: 4500),
            NewScene(2, durationMs: 15500, facts: [new SceneFact(1, ConfirmedFact, "Pin 30 giờ")]),
        ]);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(4500, 15400)]
    [InlineData(4500, 15600)]
    public void Scene_durations_that_do_not_sum_to_the_target_are_rejected(int first, int second)
    {
        var problems = Problems([NewScene(1, durationMs: first), NewScene(2, durationMs: second)]);

        Assert.Contains("20 seconds", Assert.Single(problems));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    // Between two frames.
    [InlineData(1050)]
    public void A_Scene_that_lasts_no_time_or_ends_between_two_frames_is_rejected(int durationMs)
    {
        var problems = Problems([NewScene(1, durationMs: 20000 - durationMs), NewScene(2, durationMs)]);

        Assert.Contains(problems, problem => problem.StartsWith("Scene 2 lasts", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Scene_that_uses_an_asset_the_Product_does_not_have_is_rejected()
    {
        var problems = Problems([NewScene(1, durationMs: 4500), NewScene(2, durationMs: 15500, assets: [Photo, Guid.NewGuid()])]);

        Assert.Contains("Scene 2", Assert.Single(problems));
    }

    [Fact]
    public void A_Scene_whose_text_cites_a_Fact_that_is_not_Confirmed_is_rejected()
    {
        var problems = Problems([
            NewScene(1, durationMs: 4500, facts: [new SceneFact(1, ConfirmedFact, "Pin 30 giờ"), new SceneFact(2, Guid.NewGuid(), "Chống nước")]),
            NewScene(2, durationMs: 15500),
        ]);

        var problem = Assert.Single(problems);
        Assert.Contains("Scene 1", problem);
        Assert.Contains("Chống nước", problem);
        Assert.DoesNotContain("Pin 30 giờ", problem);
    }

    [Theory]
    [InlineData(Technique.ImageToVideo)]
    [InlineData(Technique.VideoAsset)]
    [InlineData(Technique.ThreeDRender)]
    public void A_Scene_planned_with_a_Technique_the_Render_Mode_does_not_allow_is_rejected(Technique technique)
    {
        Scene[] scenes = [NewScene(1, durationMs: 4500), NewScene(2, durationMs: 15500, technique)];

        Assert.Contains("Scene 2", Assert.Single(Problems(scenes)));
        Assert.Empty(Problems(scenes, RenderMode.Hybrid));
    }

    [Fact]
    public void A_Storyboard_with_no_Scenes_is_rejected()
    {
        Assert.Single(Problems([]));
    }

    [Fact]
    public void Every_problem_is_reported_not_only_the_first()
    {
        var problems = Problems([
            NewScene(1, durationMs: 4500, assets: [Guid.NewGuid()]),
            NewScene(2, durationMs: 1000, Technique.ImageToVideo),
        ]);

        Assert.Equal(3, problems.Count);
    }

    private static IReadOnlyList<string> Problems(IReadOnlyList<Scene> scenes, RenderMode renderMode = RenderMode.ProductLock) =>
        StoryboardRules.Problems(
            scenes, targetDurationSeconds: 20, renderMode,
            productAssetIds: new HashSet<Guid> { Photo }, confirmedFactIds: new HashSet<Guid> { ConfirmedFact });

    private static Scene NewScene(
        int position, int durationMs, Technique technique = Technique.ImageMotion, Guid[]? assets = null, SceneFact[]? facts = null) =>
        new(position, SceneLayout.Facts, technique, durationMs, ["Pin 30 giờ"], "Pin 30 giờ.", assets ?? [Photo], facts ?? []);
}
