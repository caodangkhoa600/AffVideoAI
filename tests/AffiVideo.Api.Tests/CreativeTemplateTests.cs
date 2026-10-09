using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What the three creative templates are to the planner, as pure functions.</summary>
public sealed class CreativeTemplateTests
{
    private static readonly Guid Photo = Guid.NewGuid();

    public static TheoryData<CreativeTemplate> All => [.. Enum.GetValues<CreativeTemplate>()];

    [Theory]
    [MemberData(nameof(All))]
    public void Every_creative_template_is_versioned_opens_on_the_Hook_and_gives_each_Scene_a_layout_of_its_own(CreativeTemplate which)
    {
        var template = CreativeTemplates.Find(which)!;

        Assert.Equal(which, template.Template);
        Assert.True(template.Version >= 1);
        Assert.Equal(SceneLayout.Hook, template.Scenes[0].Layout);
        Assert.Equal(template.Scenes.Count, template.Scenes.Select(scene => scene.Layout).Distinct().Count());
        Assert.Contains(template.Scenes, scene => scene.Layout == SceneLayout.Facts);
        Assert.Equal(SceneLayout.Closing, template.Scenes[^1].Layout);
    }

    [Fact]
    public void No_two_creative_templates_share_a_Scene_structure()
    {
        var structures = Enum.GetValues<CreativeTemplate>()
            .Select(which => CreativeTemplates.Find(which)!)
            .Select(template => string.Join(",", template.Scenes.Select(scene => (scene.Layout, scene.Share))))
            .ToList();

        Assert.Equal(structures.Count, structures.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(All))]
    public void Every_Scene_is_given_the_time_its_layout_needs_at_every_target_duration(CreativeTemplate which)
    {
        var template = CreativeTemplates.Find(which)!;

        for (var seconds = 15; seconds <= 30; seconds++)
        {
            var durations = template.SceneDurations(seconds);
            Assert.Equal(seconds * 1000, durations.Sum());
            Assert.All(template.Scenes.Zip(durations), scene => Assert.True(scene.Second >= scene.First.MinDurationMs));
            // At least one Fact of a few words can be read.
            Assert.True(template.MaxFactWords(seconds, factCount: 1) >= 8);
        }
    }

    [Fact]
    public void Luxury_Cinematic_holds_less_text_than_the_other_creative_templates()
    {
        var luxury = CreativeTemplates.Find(CreativeTemplate.LuxuryCinematic)!;

        Assert.All(
            [CreativeTemplates.Find(CreativeTemplate.ProductShowcase)!, CreativeTemplates.Find(CreativeTemplate.ProblemSolution)!],
            other =>
            {
                Assert.True(luxury.Scenes.Count < other.Scenes.Count);
                Assert.True(luxury.MaxFacts < other.MaxFacts);
                Assert.True(luxury.FactLimit.MaxCharacters < other.FactLimit.MaxCharacters);
            });
    }

    [Fact]
    public void A_Solution_Scene_sets_a_label_and_the_Product_name()
    {
        var template = CreativeTemplates.Find(CreativeTemplate.ProblemSolution)!;

        Assert.Empty(StoryboardRules.LayoutProblems([Solution("Giải pháp", "Lumo 500")], template));
        Assert.Contains("exactly 2", Assert.Single(StoryboardRules.LayoutProblems([Solution("Lumo 500")], template)));
        Assert.StartsWith(
            "Scene 2's label is too long for its layout",
            Assert.Single(StoryboardRules.LayoutProblems([Solution("Giải pháp cho mọi nhà đây rồi", "Lumo 500")], template)));
        Assert.StartsWith(
            "Scene 2's name is too long for its layout",
            Assert.Single(StoryboardRules.LayoutProblems([Solution("Giải pháp", new string('a', 61))], template)));
    }

    private static Scene Solution(params string[] lines) =>
        new(2, SceneLayout.Solution, Technique.ImageMotion, 4000, lines, "", [Photo], []);
}
