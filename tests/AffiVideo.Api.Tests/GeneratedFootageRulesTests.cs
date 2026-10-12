using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Generated footage as pure functions: which Scenes the planning engine generates, what the
/// creative templates ask a provider for, the Fact a generated Scene has to rest on, and what a Clip costs.
/// </summary>
public sealed class GeneratedFootageRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Photo = Guid.NewGuid();

    public static TheoryData<CreativeTemplate> All => [.. Enum.GetValues<CreativeTemplate>()];

    [Fact]
    public void Each_creative_template_states_which_of_its_Scenes_may_be_generated_and_with_which_Technique()
    {
        Assert.Equal(
            [(SceneLayout.Hook, Technique.GeneratedBackdrop), (SceneLayout.Facts, Technique.GeneratedFootage)],
            Generated(CreativeTemplate.ProductShowcase));
        Assert.Equal(
            [(SceneLayout.Hook, Technique.GeneratedBackdrop), (SceneLayout.Facts, Technique.GeneratedFootage)],
            Generated(CreativeTemplate.LuxuryCinematic));
        // Its Hook is a problem stated with no Product in the frame, so what is generated there is footage.
        Assert.Equal(
            [(SceneLayout.Hook, Technique.GeneratedFootage), (SceneLayout.Facts, Technique.GeneratedFootage)],
            Generated(CreativeTemplate.ProblemSolution));
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_Scene_that_may_be_generated_has_a_description_and_can_be_made_another_way(CreativeTemplate which)
    {
        var template = CreativeTemplates.Find(which)!;

        foreach (var slot in template.Scenes)
        {
            var generated = slot.Techniques.Any(technique => technique.IsGenerated());
            Assert.Equal(generated, slot.Description is not null);
            // With no provider the Scene is still planned: what it would rather be is never all it can be.
            Assert.False(slot.Techniques[^1].IsGenerated());
        }
    }

    [Theory]
    [MemberData(nameof(All))]
    public void The_descriptions_of_generated_footage_ask_for_invented_adults_only_and_for_no_Product(CreativeTemplate which)
    {
        var template = CreativeTemplates.Find(which)!;

        var footage = template.Scenes.Where(slot => slot.Techniques.Contains(Technique.GeneratedFootage)).ToList();

        Assert.NotEmpty(footage);
        Assert.All(footage, slot =>
        {
            var description = slot.DescriptionFor("Bình giữ nhiệt", "Dân văn phòng 25-35 tuổi")!;
            Assert.Contains("invented adult", description);
            Assert.Contains("no real or recognisable person", description);
            Assert.Contains("no child", description);
            Assert.Contains("no brand mascot", description);
            Assert.Contains("No product", description);
            // What a member typed is placed in it whole.
            Assert.Contains("Bình giữ nhiệt", description);
            Assert.Contains("Dân văn phòng 25-35 tuổi", description);
            Assert.True(description.Length <= StoryboardRules.DescriptionMaxLength);
        });
    }

    [Theory]
    [InlineData(CreativeTemplate.ProductShowcase, new[] { Technique.GeneratedBackdrop, Technique.ImageMotion, Technique.GeneratedFootage, Technique.ImageMotion })]
    [InlineData(CreativeTemplate.LuxuryCinematic, new[] { Technique.GeneratedBackdrop, Technique.GeneratedFootage, Technique.ImageMotion })]
    [InlineData(CreativeTemplate.ProblemSolution, new[] { Technique.GeneratedFootage, Technique.ImageMotion, Technique.GeneratedFootage, Technique.ImageMotion })]
    public void Hybrid_generates_the_Scenes_its_template_allows_when_Stills_and_Clips_can_be_made(CreativeTemplate which, Technique[] expected)
    {
        var techniques = PlanningEngine.AssignTechniques(
            usablePhotos: 2, CreativeTemplates.Find(which)!, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: true, canGenerateClips: true);

        Assert.Equal(expected, techniques);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void With_nothing_to_make_a_Clip_with_no_Scene_is_generated_footage(CreativeTemplate which)
    {
        var template = CreativeTemplates.Find(which)!;

        var techniques = PlanningEngine.AssignTechniques(
            usablePhotos: 2, template, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: true, canGenerateClips: false);
        // A Clip starts from a Still, so a video provider alone makes nothing.
        var withoutStills = PlanningEngine.AssignTechniques(
            usablePhotos: 2, template, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: false, canGenerateClips: true);

        Assert.DoesNotContain(Technique.GeneratedFootage, techniques);
        Assert.All(withoutStills, technique => Assert.False(technique.IsGenerated()));
    }

    [Fact]
    public void At_most_two_Scenes_are_generated_and_the_Hook_Scene_is_the_first_of_them()
    {
        // Four Scenes that would each rather be generated, the Hook's last of all.
        Technique[] backdrop = [Technique.GeneratedBackdrop, Technique.ImageMotion];
        Technique[] footage = [Technique.GeneratedFootage, Technique.TextAnimation];
        var template = CreativeTemplates.Find(CreativeTemplate.ProductShowcase)! with
        {
            Scenes =
            [
                new SceneSlot(SceneLayout.Reveal, Share: 1, backdrop, MinDurationMs: 1000, Description: "A"),
                new SceneSlot(SceneLayout.Facts, Share: 1, footage, MinDurationMs: 1000, Description: "B"),
                new SceneSlot(SceneLayout.Closing, Share: 1, backdrop, MinDurationMs: 1000, Description: "C"),
                new SceneSlot(SceneLayout.Hook, Share: 1, backdrop, MinDurationMs: 1000, Description: "D"),
            ],
        };

        var techniques = PlanningEngine.AssignTechniques(
            usablePhotos: 1, template, targetDurationSeconds: 20, RenderMode.Hybrid, canGenerateStills: true, canGenerateClips: true);

        Assert.Equal(2, PlanningEngine.MaxGeneratedScenes);
        Assert.Equal([Technique.GeneratedBackdrop, Technique.TextAnimation, Technique.ImageMotion, Technique.GeneratedBackdrop], techniques);
    }

    [Fact]
    public void A_Storyboard_with_more_than_two_generated_Scenes_is_not_acceptable()
    {
        var scenes = Enumerable.Range(1, 3)
            .Select(position => new Scene(
                position, SceneLayout.Hook, Technique.GeneratedBackdrop, 5000, ["Lumo 500"], "", [Photo], [], description: "Bàn gỗ sồi"))
            .ToList();

        var problems = StoryboardRules.Problems(scenes, targetDurationSeconds: 15, RenderMode.Hybrid, new HashSet<Guid> { Photo }, new HashSet<Guid>());

        Assert.Equal(["3 Scenes are generated, and a Hybrid video has at most 2."], problems);
    }

    [Fact]
    public void A_generated_Scene_that_shows_what_the_Product_does_has_to_cite_a_Confirmed_Fact()
    {
        var template = CreativeTemplates.Find(CreativeTemplate.ProductShowcase)!;
        var fact = Guid.NewGuid();
        // The Facts Scene's footage is read with the Facts set over it. A backdrop is a setting, and says nothing.
        var hook = new Scene(1, SceneLayout.Hook, Technique.GeneratedBackdrop, 3000, ["Bạn vẫn dùng bình nhựa?"], "", [Photo], [], description: "Bàn gỗ sồi");
        var resting = Footage(2, SceneLayout.Facts, [new SceneFact(1, fact, "Giữ lạnh suốt 24 giờ")]);
        var onNothing = Footage(3, SceneLayout.Facts, []);

        Assert.True(template.Scenes.Single(slot => slot.Layout == SceneLayout.Facts).ShowsWhatTheProductDoes);
        Assert.False(template.Scenes.Single(slot => slot.Layout == SceneLayout.Hook).ShowsWhatTheProductDoes);
        Assert.Empty(StoryboardRules.DescriptionProblems([hook, resting], template));
        Assert.Equal(
            [
                "Scene 3's generated picture shows what the Product does, so the Scene has to rest on a Confirmed Fact, and it cites none. " +
                "Regenerate the Scene, so that it is written from the Product's Confirmed Facts again.",
            ],
            StoryboardRules.DescriptionProblems([hook, onNothing], template));
    }

    [Fact]
    public void A_problem_shown_in_generated_footage_says_nothing_of_the_Product_and_needs_no_Fact()
    {
        var template = CreativeTemplates.Find(CreativeTemplate.ProblemSolution)!;

        var problems = StoryboardRules.DescriptionProblems([Footage(1, SceneLayout.Hook, [])], template);

        Assert.Empty(problems);
    }

    [Fact]
    public void Generated_footage_that_refers_to_an_approved_Clip_can_be_rendered()
    {
        var photo = Asset();
        var clip = Guid.NewGuid();
        var storyboard = Storyboard([Footage(1, SceneLayout.Facts, [], photo.Id, clip)]);

        var problems = StoryboardRules.RenderProblems(storyboard, [photo], new HashSet<Guid>(), new HashSet<Guid> { clip });

        Assert.Empty(problems);
    }

    [Fact]
    public void Generated_footage_with_no_approved_Clip_is_named_and_the_member_is_told_what_to_do()
    {
        var photo = Asset();
        // The first refers to no Clip. The second refers to one that is not approved.
        var storyboard = Storyboard([Footage(1, SceneLayout.Hook, [], photo.Id), Footage(2, SceneLayout.Facts, [], photo.Id, Guid.NewGuid())]);

        var problems = StoryboardRules.RenderProblems(storyboard, [photo], new HashSet<Guid>(), new HashSet<Guid> { Guid.NewGuid() });

        Assert.Equal(
            [
                "Scene 1 is generated footage and has no approved Clip.",
                "Scene 2 is generated footage and has no approved Clip.",
                "Approve a Clip for it at Storyboard review, then render the version that makes.",
            ],
            problems);
    }

    [Theory]
    [InlineData(0.10, 5200, 0.52)]
    [InlineData(0.10, 3000, 0.30)]
    // Kept to six decimals, as every estimate is.
    [InlineData(0.0333333, 1100, 0.036667)]
    [InlineData(0, 7000, 0)]
    public void A_Clip_is_estimated_at_the_rate_for_a_second_for_as_long_as_its_Scene_lasts(decimal perSecond, int durationMs, decimal expected)
    {
        Assert.Equal(expected, ProductionCosts.ClipEstimate(perSecond, durationMs));
    }

    [Fact]
    public void A_Clip_waits_for_its_Still_and_is_only_queued_from_one_that_exists()
    {
        var waiting = NewClip(stillId: null);
        var still = Guid.NewGuid();

        Assert.Equal(ClipState.WaitingForStill, waiting.State);
        Assert.Null(waiting.StillId);
        // What is confirmed counts toward the ceiling from the moment it is confirmed.
        Assert.True(waiting.CountsAsSpent);
        Assert.True(waiting.StartFrom(still, Now));
        Assert.Equal((ClipState.Queued, (Guid?)still), (waiting.State, waiting.StillId));
        // It starts from one Still, once.
        Assert.False(waiting.StartFrom(Guid.NewGuid(), Now));
        Assert.Equal(still, waiting.StillId);
        Assert.Equal(ClipState.Queued, NewClip(still).State);
    }

    [Theory]
    [InlineData(ClipState.WaitingForStill, ClipState.Queued, true)]
    [InlineData(ClipState.WaitingForStill, ClipState.Generating, false)]
    [InlineData(ClipState.Queued, ClipState.Generating, true)]
    [InlineData(ClipState.Generating, ClipState.ReadyForReview, true)]
    [InlineData(ClipState.Generating, ClipState.Queued, true)]
    [InlineData(ClipState.Generating, ClipState.Failed, true)]
    [InlineData(ClipState.Queued, ClipState.ReadyForReview, false)]
    [InlineData(ClipState.ReadyForReview, ClipState.Approved, true)]
    [InlineData(ClipState.Failed, ClipState.Queued, false)]
    [InlineData(ClipState.Approved, ClipState.ReadyForReview, false)]
    public void A_Clip_changes_state_only_as_a_paid_generation_may(ClipState from, ClipState to, bool allowed)
    {
        Assert.Equal(allowed, from.CanBecome(to));
    }

    [Fact]
    public void A_Clip_is_of_a_Scene_that_describes_its_picture_rests_on_the_same_Facts_and_is_no_longer_than_it()
    {
        var fact = Guid.NewGuid();
        var another = Guid.NewGuid();
        var scene = Footage(1, SceneLayout.Facts, [new SceneFact(1, fact, "Giữ lạnh suốt 24 giờ"), new SceneFact(2, another, "Không chứa BPA")]);
        var still = Guid.NewGuid();

        // Whichever order the Scene uses its Facts in.
        Assert.True(NewClip(still, scene.Description!, [another, fact], scene.DurationMs).Shows(scene));
        // A longer Clip is cut where the Scene ends.
        Assert.True(NewClip(still, scene.Description!, [fact, another], scene.DurationMs + 800).Shows(scene));
        Assert.False(NewClip(still, scene.Description!, [fact], scene.DurationMs).Shows(scene));
        Assert.False(NewClip(still, scene.Description!, [fact, another, Guid.NewGuid()], scene.DurationMs).Shows(scene));
        Assert.False(NewClip(still, "Phố đêm", [fact, another], scene.DurationMs).Shows(scene));
        Assert.False(NewClip(still, scene.Description!, [fact, another], scene.DurationMs - 100).Shows(scene));
        // Only generated footage is a Clip.
        var backdrop = new Scene(1, SceneLayout.Hook, Technique.GeneratedBackdrop, 5000, ["Lumo 500"], "", [Photo], [], description: "Phố đêm");
        Assert.False(NewClip(still, "Phố đêm", [], 5000).Shows(backdrop));
    }

    private static List<(SceneLayout, Technique)> Generated(CreativeTemplate which) =>
        [.. CreativeTemplates.Find(which)!.Scenes
            .Where(slot => slot.Techniques.Any(technique => technique.IsGenerated()))
            .Select(slot => (slot.Layout, slot.Techniques.Single(technique => technique.IsGenerated())))];

    private static Scene Footage(int position, SceneLayout layout, IEnumerable<SceneFact> facts, Guid? photo = null, Guid? clip = null) =>
        new(position, layout, Technique.GeneratedFootage, 5000, ["Giữ lạnh suốt 24 giờ"], "", [photo ?? Photo], facts,
            description: "Một người uống nước mát giữa trưa hè", clipId: clip);

    private static Storyboard Storyboard(IEnumerable<Scene> scenes) =>
        new(Guid.NewGuid(), Organization, Guid.NewGuid(), 1, CreativeTemplate.ProblemSolution, 1, StoryboardPlanner.Mock, RenderMode.Hybrid, scenes, Now);

    private static ProductAsset Asset() => new(Guid.NewGuid(), Organization, Guid.NewGuid(), ProductAssetKind.Photo, 600, 800, 1000, Now);

    private static Clip NewClip(Guid? stillId, string description = "Phố đêm", Guid[]? factIds = null, int durationMs = 5000) =>
        new(Guid.NewGuid(), Organization, Guid.NewGuid(), Guid.NewGuid(), stillId, description, factIds ?? [], durationMs, VideoProvider.Fake,
            new EstimatedAmount(0.5m, "USD"), "1", Guid.NewGuid().ToString(), 1, Guid.NewGuid(), Now);
}
