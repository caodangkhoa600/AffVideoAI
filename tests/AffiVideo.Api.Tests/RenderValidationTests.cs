using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What stops a Storyboard version being rendered, as a pure function.</summary>
public sealed class RenderValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void A_Product_Showcase_Storyboard_whose_photos_the_Product_still_has_can_be_rendered()
    {
        var photo = Asset();

        var problems = StoryboardRules.RenderProblems(Storyboard([NewScene(1, photo.Id), NewScene(2, photo.Id)]), [photo], Approved(), NoClips);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_Scene_whose_photo_has_been_removed_is_named_and_the_member_is_told_what_to_do()
    {
        var photo = Asset();

        var problems = StoryboardRules.RenderProblems(Storyboard([NewScene(1, photo.Id), NewScene(2, Guid.NewGuid())]), [photo], Approved(), NoClips);

        Assert.Equal(
            ["Scene 2 shows a photo that has since been removed from the Product.", "Generate the Storyboard again, then render that version."],
            problems);
    }

    [Fact]
    public void A_Scene_cannot_show_the_logo_or_a_photo_too_small_for_a_video()
    {
        var logo = Asset(ProductAssetKind.Logo);
        var small = Asset(width: ProductAsset.MinVideoPhotoSide - 1);

        var problems = StoryboardRules.RenderProblems(Storyboard([NewScene(1, logo.Id), NewScene(2, small.Id)]), [logo, small], Approved(), NoClips);

        Assert.Equal(2, problems.Count);
        Assert.StartsWith("Scene 1 shows an image", problems[0]);
        Assert.StartsWith("Scene 2 shows an image", problems[1]);
    }

    [Fact]
    public void A_Scene_shows_exactly_one_photo()
    {
        var photo = Asset();

        var problems = StoryboardRules.RenderProblems(
            Storyboard([NewScene(1, photo.Id, photo.Id), NewScene(2)]), [photo], Approved(), NoClips);

        Assert.Equal(2, problems.Count);
        Assert.StartsWith("Scene 1 shows 2 photos", problems[0]);
        Assert.StartsWith("Scene 2 shows 0 photos", problems[1]);
    }

    [Theory]
    [InlineData(CreativeTemplate.LuxuryCinematic)]
    [InlineData(CreativeTemplate.ProductShowcase)]
    [InlineData(CreativeTemplate.ProblemSolution)]
    public void A_Storyboard_of_any_creative_template_can_be_rendered_in_Product_Lock_and_in_Hybrid(CreativeTemplate template)
    {
        var photo = Asset();

        var locked = StoryboardRules.RenderProblems(Storyboard([NewScene(1, photo.Id)], template), [photo], Approved(), NoClips);
        var hybrid = StoryboardRules.RenderProblems(Storyboard([NewScene(1, photo.Id)], template, RenderMode.Hybrid), [photo], Approved(), NoClips);

        Assert.Empty(locked);
        Assert.Empty(hybrid);
    }

    [Fact]
    public void A_generated_backdrop_that_refers_to_an_approved_Still_can_be_rendered()
    {
        var photo = Asset();
        var still = Guid.NewGuid();

        var problems = StoryboardRules.RenderProblems(
            Storyboard([Backdrop(1, photo.Id, still), NewScene(2, photo.Id)], renderMode: RenderMode.Hybrid), [photo], Approved(still), NoClips);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_generated_backdrop_with_no_approved_Still_is_named_and_the_member_is_told_what_to_do()
    {
        var photo = Asset();
        // The first refers to no Still. The second refers to one that is not approved: not yet, or no longer.
        var scenes = new[] { Backdrop(1, photo.Id, still: null), Backdrop(2, photo.Id, Guid.NewGuid()), NewScene(3, photo.Id) };

        var problems = StoryboardRules.RenderProblems(Storyboard(scenes, renderMode: RenderMode.Hybrid), [photo], Approved(Guid.NewGuid()), NoClips);

        Assert.Equal(
            [
                "Scene 1 has a generated backdrop and no approved Still.",
                "Scene 2 has a generated backdrop and no approved Still.",
                "Approve a Still for it at Storyboard review, then render the version that makes.",
            ],
            problems);
    }

    [Fact]
    public void A_Scene_whose_layout_its_creative_template_does_not_draw_is_not_rendered()
    {
        var photo = Asset();
        var solution = new Scene(1, SceneLayout.Solution, Technique.ImageMotion, 5000, ["Giải pháp", "Lumo 500"], "", [photo.Id], []);

        var problems = StoryboardRules.RenderProblems(Storyboard([solution], CreativeTemplate.LuxuryCinematic), [photo], Approved(), NoClips);

        Assert.Contains("Solution", Assert.Single(problems));
    }

    [Fact]
    public void A_Scene_planned_with_a_generative_Technique_is_not_rendered_in_Product_Lock()
    {
        var photo = Asset();
        var generative = new Scene(1, SceneLayout.Reveal, Technique.GeneratedFootage, 5000, ["Lumo 500"], "", [photo.Id], []);

        var problems = StoryboardRules.RenderProblems(Storyboard([generative]), [photo], Approved(), NoClips);

        Assert.Contains("GeneratedFootage", Assert.Single(problems));
    }

    private static Storyboard Storyboard(
        IEnumerable<Scene> scenes, CreativeTemplate template = CreativeTemplate.ProductShowcase, RenderMode renderMode = RenderMode.ProductLock) =>
        new(Guid.NewGuid(), Organization, Guid.NewGuid(), 1, template, 1, StoryboardPlanner.Mock, renderMode, scenes, Now);

    private static Scene Backdrop(int position, Guid asset, Guid? still) =>
        new(position, SceneLayout.Hook, Technique.GeneratedBackdrop, 5000, ["Bạn vẫn dùng bình nhựa?"], "", [asset], [],
            description: "Bàn gỗ sồi bên cửa sổ", stillId: still);

    private static readonly HashSet<Guid> NoClips = [];

    /// <summary>The Stills the Scenes refer to that a member has approved.</summary>
    private static HashSet<Guid> Approved(params Guid[] stills) => [.. stills];

    private static Scene NewScene(int position, params Guid[] assets) =>
        new(position, SceneLayout.Hook, Technique.ImageMotion, 5000, ["Bạn vẫn dùng bình nhựa?"], "", assets, []);

    private static ProductAsset Asset(ProductAssetKind kind = ProductAssetKind.Photo, int width = 600) =>
        new(Guid.NewGuid(), Organization, Product, kind, width, 800, 1000, Now);
}
