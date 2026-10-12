using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>The planning engine as a pure function: which Technique each Scene gets.</summary>
public sealed class TechniqueSelectionTests
{
    private static readonly CreativeTemplateDefinition ProductShowcase = CreativeTemplates.Find(CreativeTemplate.ProductShowcase)!;

    [Theory]
    [InlineData(Technique.StaticImage, true)]
    [InlineData(Technique.ImageMotion, true)]
    [InlineData(Technique.TextAnimation, true)]
    [InlineData(Technique.GeneratedBackdrop, false)]
    [InlineData(Technique.GeneratedFootage, false)]
    [InlineData(Technique.VideoAsset, false)]
    [InlineData(Technique.ThreeDRender, false)]
    public void Product_Lock_allows_only_static_image_image_motion_and_text_animation(Technique technique, bool allowed)
    {
        Assert.Equal(allowed, RenderMode.ProductLock.Allows(technique));
        Assert.True(RenderMode.Hybrid.Allows(technique));
    }

    [Fact]
    public void Product_Showcase_moves_the_Product_in_every_Scene_but_the_Facts_where_type_is_animated()
    {
        var techniques = PlanningEngine.AssignTechniques(usablePhotos: 2, ProductShowcase, targetDurationSeconds: 20, RenderMode.ProductLock);

        Assert.Equal(
            [Technique.ImageMotion, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            techniques);
    }

    [Fact]
    public void Every_Scene_of_every_duration_gets_a_Technique_that_Product_Lock_allows()
    {
        for (var seconds = Project.MinTargetDurationSeconds; seconds <= Project.MaxTargetDurationSeconds; seconds++)
        {
            for (var photos = 0; photos <= 3; photos++)
            {
                var techniques = PlanningEngine.AssignTechniques(photos, ProductShowcase, seconds, RenderMode.ProductLock);

                Assert.Equal(ProductShowcase.Scenes.Count, techniques.Count);
                Assert.All(techniques, technique => Assert.True(RenderMode.ProductLock.Allows(technique)));
            }
        }
    }

    [Theory]
    [InlineData(RenderMode.ProductLock)]
    // Hybrid allows generative Techniques, but there is no provider to make them with yet.
    [InlineData(RenderMode.Hybrid)]
    public void A_Scene_that_would_rather_be_generated_falls_back_to_what_can_be_made(RenderMode renderMode)
    {
        var template = TemplateOf(
            new SceneSlot(SceneLayout.Reveal, Share: 1, [Technique.GeneratedFootage, Technique.ThreeDRender, Technique.VideoAsset, Technique.ImageMotion], MinDurationMs: 1000));

        var techniques = PlanningEngine.AssignTechniques(usablePhotos: 1, template, targetDurationSeconds: 20, renderMode);

        Assert.Equal([Technique.ImageMotion], techniques);
    }

    [Fact]
    public void A_Scene_too_short_for_a_move_to_be_seen_holds_the_image_still()
    {
        // One second and nineteen seconds.
        var template = TemplateOf(
            new SceneSlot(SceneLayout.Hook, Share: 1, [Technique.ImageMotion, Technique.StaticImage], MinDurationMs: 1000),
            new SceneSlot(SceneLayout.Reveal, Share: 19, [Technique.ImageMotion, Technique.StaticImage], MinDurationMs: 1000));

        var techniques = PlanningEngine.AssignTechniques(usablePhotos: 1, template, targetDurationSeconds: 20, RenderMode.ProductLock);

        Assert.Equal([Technique.StaticImage, Technique.ImageMotion], techniques);
    }

    [Fact]
    public void With_no_photo_only_type_is_left_to_animate()
    {
        var techniques = PlanningEngine.AssignTechniques(usablePhotos: 0, ProductShowcase, targetDurationSeconds: 20, RenderMode.ProductLock);

        Assert.All(techniques, technique => Assert.Equal(Technique.TextAnimation, technique));
    }

    [Fact]
    public void Hybrid_gives_the_Hook_Scene_a_generated_backdrop_when_a_Still_can_be_generated()
    {
        var techniques = PlanningEngine.AssignTechniques(
            usablePhotos: 2, ProductShowcase, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: true);

        Assert.Equal(
            [Technique.GeneratedBackdrop, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            techniques);
    }

    [Theory]
    // Product Lock generates nothing, whatever exists to generate with.
    [InlineData(RenderMode.ProductLock, true)]
    // With no image provider Hybrid plans exactly what Product Lock does.
    [InlineData(RenderMode.Hybrid, false)]
    public void Nothing_is_generated_in_Product_Lock_or_without_an_image_provider(RenderMode renderMode, bool canGenerateStills)
    {
        foreach (var template in Enum.GetValues<CreativeTemplate>().Select(template => CreativeTemplates.Find(template)!))
        {
            var techniques = PlanningEngine.AssignTechniques(
                usablePhotos: 2, template, targetDurationSeconds: 20, renderMode, canGenerateStills);

            Assert.Equal(PlanningEngine.AssignTechniques(usablePhotos: 2, template, targetDurationSeconds: 20, RenderMode.ProductLock), techniques);
            Assert.All(techniques, technique => Assert.True(RenderMode.ProductLock.Allows(technique)));
        }
    }

    [Fact]
    public void A_Hook_Scene_with_no_Product_in_it_gets_no_generated_backdrop()
    {
        var problemSolution = CreativeTemplates.Find(CreativeTemplate.ProblemSolution)!;

        var techniques = PlanningEngine.AssignTechniques(
            usablePhotos: 2, problemSolution, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: true);

        Assert.Equal(
            [Technique.TextAnimation, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            techniques);
    }

    [Fact]
    public void The_Technique_list_has_generated_backdrop_and_generated_footage_and_no_image_to_video()
    {
        Assert.Equal(
            ["StaticImage", "ImageMotion", "GeneratedBackdrop", "GeneratedFootage", "VideoAsset", "TextAnimation", "ThreeDRender"],
            Enum.GetNames<Technique>());
    }

    private static CreativeTemplateDefinition TemplateOf(params SceneSlot[] scenes) => ProductShowcase with { Scenes = scenes };
}
