using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What an edit makes of a Storyboard's Scenes, and what a layout holds, as pure functions.</summary>
public sealed class StoryboardEditRulesTests
{
    private static readonly Guid Photo = Guid.NewGuid();
    private static readonly Guid OtherPhoto = Guid.NewGuid();
    private static readonly Guid Fact = Guid.NewGuid();
    private static readonly CreativeTemplateDefinition Template = CreativeTemplates.Find(CreativeTemplate.ProductShowcase)!;

    // A 20-second Product Showcase Storyboard as the mock planner writes it.
    private static readonly Scene[] Planned =
    [
        new(1, SceneLayout.Hook, Technique.ImageMotion, 3000, ["Bạn vẫn dùng bình nhựa?"], "Bạn vẫn dùng bình nhựa?", [Photo], []),
        new(2, SceneLayout.Reveal, Technique.ImageMotion, 5000, ["Lumo 500"], "Đây là Lumo 500.", [Photo], []),
        new(3, SceneLayout.Facts, Technique.TextAnimation, 7000, ["Giữ lạnh 24 giờ"], "Giữ lạnh 24 giờ.", [Photo], [new SceneFact(1, Fact, "Giữ lạnh 24 giờ")]),
        new(4, SceneLayout.Closing, Technique.ImageMotion, 5000, ["Lumo 500", "Xem chi tiết sản phẩm"], "Lumo 500. Xem chi tiết sản phẩm ngay hôm nay.", [Photo], []),
    ];

    [Fact]
    public void Scenes_that_are_named_and_not_changed_come_out_as_they_were()
    {
        var edited = StoryboardEditing.Apply(Planned, [new(1), new(2), new(3), new(4)]);

        Assert.Equal(Planned.Select(Described), edited.Select(Described));
        Assert.All(edited, scene => Assert.False(scene.ManuallyEdited));
        Assert.False(StoryboardEditing.Differ(Planned, edited));
    }

    [Fact]
    public void A_Scene_whose_on_screen_text_a_person_changed_is_Manually_Edited_and_no_longer_cites_Facts()
    {
        var edited = StoryboardEditing.Apply(Planned, [new(1), new(2), new(3, OnScreenText: ["Lạnh cả ngày"]), new(4)]);

        Assert.Equal(["Lạnh cả ngày"], edited[2].OnScreenText);
        Assert.Equal("Giữ lạnh 24 giờ.", edited[2].NarrationText);
        Assert.True(edited[2].ManuallyEdited);
        Assert.Empty(edited[2].Facts);
        Assert.All([edited[0], edited[1], edited[3]], scene => Assert.False(scene.ManuallyEdited));
        Assert.True(StoryboardEditing.Differ(Planned, edited));
    }

    [Fact]
    public void A_Scene_whose_narration_a_person_changed_is_Manually_Edited()
    {
        var edited = StoryboardEditing.Apply(Planned, [new(1), new(2, NarrationText: "Xin giới thiệu Lumo 500."), new(3), new(4)]);

        Assert.Equal("Xin giới thiệu Lumo 500.", edited[1].NarrationText);
        Assert.True(edited[1].ManuallyEdited);
    }

    [Fact]
    public void Text_sent_back_as_it_was_is_not_an_edit_and_spaces_around_it_do_not_count()
    {
        var edited = StoryboardEditing.Apply(
            Planned, [new(1), new(2), new(3, OnScreenText: ["  Giữ lạnh 24 giờ "], NarrationText: "Giữ lạnh 24 giờ. "), new(4)]);

        Assert.False(edited[2].ManuallyEdited);
        Assert.Equal(Fact, Assert.Single(edited[2].Facts).FactId);
        Assert.False(StoryboardEditing.Differ(Planned, edited));
    }

    [Fact]
    public void A_Scene_stays_Manually_Edited_through_later_edits_of_other_things()
    {
        var first = StoryboardEditing.Apply(Planned, [new(1), new(2, OnScreenText: ["Lumo"]), new(3), new(4)]);

        var second = StoryboardEditing.Apply(first, [new(1), new(2, DurationMs: 4000), new(3, DurationMs: 8000), new(4)]);

        Assert.True(second[1].ManuallyEdited);
        Assert.Equal(["Lumo"], second[1].OnScreenText);
    }

    [Fact]
    public void A_new_image_or_duration_changes_only_that_and_does_not_mark_the_Scene()
    {
        var edited = StoryboardEditing.Apply(
            Planned, [new(1), new(2, AssetId: OtherPhoto, DurationMs: 4000), new(3, DurationMs: 8000), new(4)]);

        Assert.Equal([OtherPhoto], edited[1].AssetIds);
        Assert.Equal([3000, 4000, 8000, 5000], edited.Select(scene => scene.DurationMs));
        Assert.All(edited, scene => Assert.False(scene.ManuallyEdited));
        Assert.Equal(Fact, Assert.Single(edited[2].Facts).FactId);
        Assert.True(StoryboardEditing.Differ(Planned, edited));
    }

    [Fact]
    public void Scenes_play_in_the_order_the_edit_names_them_and_are_counted_from_one_again()
    {
        var edited = StoryboardEditing.Apply(Planned, [new(1), new(3), new(2), new(4)]);

        Assert.Equal([1, 2, 3, 4], edited.Select(scene => scene.Position));
        Assert.Equal([SceneLayout.Hook, SceneLayout.Facts, SceneLayout.Reveal, SceneLayout.Closing], edited.Select(scene => scene.Layout));
        Assert.Equal([3000, 7000, 5000, 5000], edited.Select(scene => scene.DurationMs));
        Assert.True(StoryboardEditing.Differ(Planned, edited));
    }

    [Theory]
    // One left out, one named twice, one the version does not have.
    [InlineData(new[] { 1, 2, 3 })]
    [InlineData(new[] { 1, 2, 2, 4 })]
    [InlineData(new[] { 1, 2, 3, 5 })]
    public void An_edit_has_to_name_every_Scene_of_the_version_once(int[] positions)
    {
        var mismatch = StoryboardEditing.Mismatch(Planned, [.. positions.Select(position => new SceneChange(position))]);

        Assert.Contains("every Scene", mismatch);
        Assert.Null(StoryboardEditing.Mismatch(Planned, [new(4), new(1), new(3), new(2)]));
    }

    [Fact]
    public void The_Scenes_as_planned_fit_their_layouts()
    {
        Assert.Empty(StoryboardRules.LayoutProblems(Planned, Template));
    }

    [Fact]
    public void The_Hook_has_to_open_the_video()
    {
        var edited = StoryboardEditing.Apply(Planned, [new(2), new(1), new(3), new(4)]);

        Assert.Contains("opens the video", Assert.Single(StoryboardRules.LayoutProblems(edited, Template)));
    }

    [Theory]
    [InlineData(1, 1900)]
    [InlineData(2, 1900)]
    [InlineData(4, 2400)]
    public void A_Scene_shorter_than_its_layout_needs_is_rejected(int position, int durationMs)
    {
        var changes = Planned.Select(scene => new SceneChange(scene.Position, DurationMs: scene.Position == position ? durationMs : null)).ToArray();

        var problem = Assert.Single(StoryboardRules.LayoutProblems(StoryboardEditing.Apply(Planned, changes), Template));

        Assert.StartsWith($"Scene {position} lasts {durationMs / 1000m:0.#} seconds", problem);
        Assert.Contains("at least", problem);
    }

    [Theory]
    [InlineData(1, 2000)]
    [InlineData(2, 2000)]
    [InlineData(4, 2500)]
    public void A_Scene_as_short_as_its_layout_allows_is_accepted(int position, int durationMs)
    {
        var changes = Planned.Select(scene => new SceneChange(scene.Position, DurationMs: scene.Position == position ? durationMs : null)).ToArray();

        Assert.Empty(StoryboardRules.LayoutProblems(StoryboardEditing.Apply(Planned, changes), Template));
    }

    [Fact]
    public void A_Facts_Scene_too_short_for_its_Facts_to_be_read_is_rejected()
    {
        // Twelve words each. Three are read in 7 seconds and not in 5.
        string[] facts =
        [
            "Chống ồn chủ động, giảm tiếng ồn xung quanh khi di chuyển",
            "Pin nghe nhạc liên tục 30 giờ khi dùng kèm hộp sạc",
            "Kết nối Bluetooth 5.3, ghép đôi nhanh với điện thoại của bạn",
        ];
        var inSeven = StoryboardEditing.Apply(Planned, [new(1), new(2), new(3, OnScreenText: facts), new(4)]);
        var inFive = StoryboardEditing.Apply(inSeven, [new(1), new(2, DurationMs: 7000), new(3, DurationMs: 5000), new(4)]);

        Assert.Empty(StoryboardRules.LayoutProblems(inSeven, Template));
        var problem = Assert.Single(StoryboardRules.LayoutProblems(inFive, Template));
        Assert.StartsWith("Scene 3", problem);
        Assert.Contains("words", problem);
    }

    [Theory]
    // The Hook: fourteen words; the name: more than sixty characters; a Fact: more than 120.
    [InlineData(1, "Một hai ba bốn năm sáu bảy tám chín mười một hai ba bốn", "within the first two seconds")]
    [InlineData(2, "Bình giữ nhiệt Lumo 500 phiên bản đặc biệt dành cho người hay đi xa", "too long for its layout")]
    [InlineData(3, "Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu Pin dùng rất lâu", "too long for its layout")]
    [InlineData(1, "Siêuphẩmcôngnghệ đây rồi", "a word too long for its layout")]
    [InlineData(2, "", "is empty")]
    public void Edited_text_that_its_layout_does_not_hold_is_rejected(int position, string text, string reason)
    {
        var changes = Planned.Select(scene => scene.Position == position && scene.Layout != SceneLayout.Closing
            ? new SceneChange(scene.Position, OnScreenText: [text])
            : new SceneChange(scene.Position)).ToArray();

        var problem = Assert.Single(StoryboardRules.LayoutProblems(StoryboardEditing.Apply(Planned, changes), Template));

        Assert.StartsWith($"Scene {position}", problem);
        Assert.Contains(reason, problem);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(3, 4)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    public void A_Scene_with_more_or_fewer_lines_than_its_layout_sets_is_rejected(int position, int lines)
    {
        var text = Enumerable.Repeat("Lumo", lines).ToArray();
        var changes = Planned.Select(scene => new SceneChange(scene.Position, OnScreenText: scene.Position == position ? text : null)).ToArray();

        var problem = Assert.Single(StoryboardRules.LayoutProblems(StoryboardEditing.Apply(Planned, changes), Template));

        Assert.StartsWith($"Scene {position} has {lines} line", problem);
    }

    [Fact]
    public void A_call_to_action_too_long_for_its_pill_and_narration_too_long_to_be_said_are_rejected()
    {
        var edited = StoryboardEditing.Apply(Planned,
        [
            new(1, NarrationText: new string('a', StoryboardRules.NarrationMaxLength + 1)),
            new(2, NarrationText: new string('a', StoryboardRules.NarrationMaxLength)),
            new(3),
            new(4, OnScreenText: ["Lumo 500", "Xem chi tiết sản phẩm ngay hôm nay nhé"]),
        ]);

        var problems = StoryboardRules.LayoutProblems(edited, Template);

        Assert.Equal(2, problems.Count);
        Assert.StartsWith("Scene 1's narration", problems[0]);
        Assert.StartsWith("Scene 4's call to action is too long for its layout", problems[1]);
    }

    private static string Described(Scene scene) => string.Join(
        " | ", scene.Position, scene.Layout, scene.Technique, scene.DurationMs, string.Join("/", scene.OnScreenText),
        scene.NarrationText, string.Join("/", scene.AssetIds), string.Join("/", scene.Facts.Select(fact => $"{fact.Position}:{fact.FactId}:{fact.Text}")));
}
