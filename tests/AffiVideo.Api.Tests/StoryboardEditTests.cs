using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;

namespace AffiVideo.Api.Tests;

/// <summary>Editing a Storyboard as a member does: every edit is a request that makes the next version.</summary>
public sealed class StoryboardEditTests(AffiVideoApp app)
{
    private const string Battery = "Pin dùng liên tục 30 giờ";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_edit_makes_the_next_version_and_leaves_the_earlier_versions_as_they_were()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);

        var response = await EditAsync(member, variant, 1, Changing(first, new(2, OnScreenText: ["Lumo 500 mới"])));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var second = await ReadAsync<StoryboardResponse>(response);
        Assert.Equal($"{StoryboardTests.Storyboards(variant)}/2", response.Headers.Location?.OriginalString);
        Assert.Equal(2, second.Version);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            (first.VariantId, first.CreativeTemplate, first.TemplateVersion, first.Planner, first.RenderMode),
            (second.VariantId, second.CreativeTemplate, second.TemplateVersion, second.Planner, second.RenderMode));
        Assert.Equal(["Lumo 500 mới"], second.Scenes[1].OnScreenText);
        // Only what was asked for changed.
        Assert.Equal(Json(first.Scenes[1] with { OnScreenText = ["Lumo 500 mới"], ManuallyEdited = true }), Json(second.Scenes[1]));
        Assert.Equal(Json(first.Scenes.Where((_, index) => index != 1)), Json(second.Scenes.Where((_, index) => index != 1)));

        // An earlier version can be edited too, and the version made is still the Variant's next.
        var third = await EditedAsync(member, variant, 1, Changing(first, new(4, NarrationText: "Mua Lumo 500 ngay.")));

        Assert.Equal(3, third.Version);
        Assert.Equal(["Lumo 500"], third.Scenes[1].OnScreenText);
        Assert.Equal(Json(first), Json(await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1")));
        Assert.Equal(Json(second), Json(await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/2")));
        Assert.Equal([3, 2, 1], (await StoryboardTests.ListAsync(member, variant)).Items.Select(storyboard => storyboard.Version));
    }

    [Fact]
    public async Task A_Scene_whose_text_a_person_changed_is_marked_Manually_Edited_and_stays_marked()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        Assert.All(first.Scenes, scene => Assert.False(scene.ManuallyEdited));

        var second = await EditedAsync(member, variant, 1, Changing(first, new(1, NarrationText: "Bạn còn dùng bình nhựa sao?"), new(3, OnScreenText: ["Nghe nhạc cả ngày"])));
        // The durations change and the text is sent back as it is: that is no edit of the text.
        var third = await EditedAsync(member, variant, 2, Changing(second, new(2, OnScreenText: second.Scenes[1].OnScreenText, NarrationText: second.Scenes[1].NarrationText, DurationMs: 4000), new(3, DurationMs: 8000)));

        Assert.Equal([true, false, true, false], second.Scenes.Select(scene => scene.ManuallyEdited));
        Assert.Equal([true, false, true, false], third.Scenes.Select(scene => scene.ManuallyEdited));
        // Text a person wrote is no longer traced to the Facts the planner wrote it from.
        Assert.Single(first.Scenes[2].Facts);
        Assert.Empty(second.Scenes[2].Facts);
        var read = await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/3");
        Assert.Equal([true, false, true, false], read.Scenes.Select(scene => scene.ManuallyEdited));
        Assert.All((await StoryboardTests.ListAsync(member, variant)).Items.Single(listed => listed.Version == 1).Scenes, scene => Assert.False(scene.ManuallyEdited));
    }

    [Fact]
    public async Task Manually_Edited_text_is_not_checked_against_Facts()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        (await FactTests.WithdrawAsync(member, product, battery.Id)).EnsureSuccessStatusCode();

        // The Facts Scene still rests on the Fact that was withdrawn, so no new version is made around it.
        var carried = await EditAsync(member, variant, 1, Changing(first, new(2, OnScreenText: ["Lumo"])));
        // A person's own words are theirs to answer for: no Fact says this, and nothing asks one to.
        var rewritten = await EditAsync(member, variant, 1, Changing(first, new(3, OnScreenText: ["Giảm 90% chỉ hôm nay", Battery], NarrationText: "Giảm 90% chỉ hôm nay.")));

        var reason = await ReasonAsync(carried);
        Assert.Contains($"Scene 3 uses a Fact that is not Confirmed: \"{Battery}\"", reason);
        Assert.Contains("change its text yourself", reason);
        Assert.Equal(HttpStatusCode.Created, rewritten.StatusCode);
        var second = await ReadAsync<StoryboardResponse>(rewritten);
        Assert.Equal(["Giảm 90% chỉ hôm nay", Battery], second.Scenes[2].OnScreenText);
        Assert.True(second.Scenes[2].ManuallyEdited);
        Assert.Empty(second.Scenes[2].Facts);
        Assert.Equal(2, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task Scenes_are_reordered_given_another_photo_and_another_duration_in_one_edit()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        var front = await StoryboardTests.UploadPhotoAsync(member, product);
        var side = await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 20);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        Assert.Equal([front.Id, front.Id, side.Id, front.Id], first.Scenes.Select(scene => scene.AssetIds.Single()));

        // The Facts before the reveal, the reveal showing the other photo, and a second moved from one to the other.
        var second = await EditedAsync(member, variant, 1,
        [
            new(1),
            new(3, DurationMs: 8000),
            new(2, AssetId: side.Id, DurationMs: 4000),
            new(4),
        ]);

        Assert.Equal([1, 2, 3, 4], second.Scenes.Select(scene => scene.Position));
        Assert.Equal([SceneLayout.Hook, SceneLayout.Facts, SceneLayout.Reveal, SceneLayout.Closing], second.Scenes.Select(scene => scene.Layout));
        Assert.Equal([3000, 8000, 4000, 5000], second.Scenes.Select(scene => scene.DurationMs));
        Assert.Equal([front.Id, side.Id, side.Id, front.Id], second.Scenes.Select(scene => scene.AssetIds.Single()));
        Assert.Equal(first.Scenes[2].OnScreenText, second.Scenes[1].OnScreenText);
        Assert.Equal(first.Scenes[2].Facts, second.Scenes[1].Facts);
        Assert.Equal(first.Scenes[1].OnScreenText, second.Scenes[2].OnScreenText);
        Assert.All(second.Scenes, scene => Assert.False(scene.ManuallyEdited));
        Assert.Equal(Json(second), Json(await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/2")));
    }

    [Theory]
    // A second longer, a second shorter, and between two frames with the total kept.
    [InlineData(6000, 7000, "21 seconds in all, and the target duration is 20 seconds")]
    [InlineData(4000, 7000, "19 seconds in all, and the target duration is 20 seconds")]
    [InlineData(5050, 6950, "A Scene lasts a whole number of tenths of a second")]
    public async Task An_edit_that_breaks_the_total_duration_is_rejected_with_the_reason(int reveal, int facts, string reason)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);

        var response = await EditAsync(member, variant, 1, Changing(first, new(2, DurationMs: reveal), new(3, DurationMs: facts)));

        Assert.Contains(reason, await ReasonAsync(response));
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task An_edit_that_shows_an_image_the_Product_cannot_show_is_rejected_with_the_reason()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        var logo = await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Logo, ProductAssetTests.Image(SKEncodedImageFormat.Png, 600, 600));
        var small = await StoryboardTests.UploadPhotoAsync(member, product, width: 399, height: 800);
        var elsewhere = await StoryboardTests.UploadPhotoAsync(member, await StoryboardTests.NewProductAsync(member, "Một sản phẩm khác"));
        await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var first = await StoryboardTests.GeneratedAsync(member, variant);

        async Task<string> RefusedAsync(Guid assetId) =>
            await ReasonAsync(await EditAsync(member, variant, 1, Changing(first, new(2, AssetId: assetId))));

        Assert.Contains("Scene 2 uses an asset the Product does not have", await RefusedAsync(Guid.NewGuid()));
        Assert.Contains("Scene 2 uses an asset the Product does not have", await RefusedAsync(elsewhere.Id));
        Assert.Contains("Scene 2 shows an image that cannot be shown in a video", await RefusedAsync(logo.Id));
        Assert.Contains("Scene 2 shows an image that cannot be shown in a video", await RefusedAsync(small.Id));
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task A_version_whose_photo_has_been_removed_is_edited_only_by_giving_its_Scenes_another()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        var front = await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        var side = await StoryboardTests.UploadPhotoAsync(member, product);
        (await member.DeleteAsync($"{ProductAssetTests.Assets(product)}/{front.Id}")).EnsureSuccessStatusCode();

        var textOnly = await EditAsync(member, variant, 1, Changing(first, new(2, OnScreenText: ["Lumo"])));
        var replaced = await EditAsync(member, variant, 1, [.. first.Scenes.Select(scene => new SceneEditRequest(scene.Position, AssetId: side.Id))]);

        var reason = await ReasonAsync(textOnly);
        Assert.All([1, 2, 3, 4], position => Assert.Contains($"Scene {position} uses an asset the Product does not have.", reason));
        Assert.Equal(HttpStatusCode.Created, replaced.StatusCode);
        Assert.All((await ReadAsync<StoryboardResponse>(replaced)).Scenes, scene => Assert.Equal([side.Id], scene.AssetIds));
    }

    [Fact]
    public async Task An_edit_its_layouts_do_not_hold_is_rejected_with_every_reason()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);

        // The reveal before the Hook and too short to be seen, and a call to action that does not fit its pill.
        var response = await EditAsync(member, variant, 1,
        [
            new(2, DurationMs: 1500),
            new(1),
            new(3, DurationMs: 10500),
            new(4, OnScreenText: ["Lumo 500", "Xem chi tiết sản phẩm ngay hôm nay nhé"]),
        ]);

        var reason = await ReasonAsync(response);
        Assert.Contains("The Hook opens the video", reason);
        Assert.Contains("Scene 1 lasts 1.5 seconds, and a Reveal Scene needs at least 2", reason);
        Assert.Contains("Scene 4's call to action is too long for its layout", reason);
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task An_edit_has_to_name_every_Scene_once_and_change_something()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        var edits = $"{StoryboardTests.Storyboards(variant)}/1/edits";

        var nothing = await EditAsync(member, variant, 1, Unchanged(first));
        var oneLeftOut = await EditAsync(member, variant, 1, [new(1), new(2, OnScreenText: ["Lumo"]), new(3)]);
        var oneTwice = await EditAsync(member, variant, 1, [new(1), new(2), new(2, OnScreenText: ["Lumo"]), new(4)]);
        var oneTooMany = await EditAsync(member, variant, 1, [new(1), new(2), new(3), new(4), new(5, OnScreenText: ["Lumo"])]);
        var noScenes = await member.PostAsync(edits, new StoryboardEditRequest([]));
        var noList = await member.PostAsync(edits, new { });

        Assert.Contains("changes nothing", await ReasonAsync(nothing));
        foreach (var response in new[] { oneLeftOut, oneTwice, oneTooMany })
        {
            Assert.Contains("An edit names every Scene of the version once", await ReasonAsync(response));
        }
        Assert.Equal(HttpStatusCode.BadRequest, noScenes.StatusCode);
        Assert.Equal(["scenes"], (await ReadAsync<HttpValidationProblemDetails>(noScenes)).Errors.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, noList.StatusCode);
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task Regenerating_one_Scene_plans_it_again_and_leaves_the_other_Scenes_untouched()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        var front = await StoryboardTests.UploadPhotoAsync(member, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 20);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        // The reveal and the Facts in a person's words, and a second moved from the reveal to the Facts.
        var second = await EditedAsync(member, variant, 1, Changing(first, new(2, OnScreenText: ["Lumo mới"], DurationMs: 4000), new(3, OnScreenText: ["Nghe nhạc cả ngày"], NarrationText: "Nghe nhạc cả ngày.", DurationMs: 8000)));
        // What the Product has now: another Confirmed Fact, and a newer photo.
        var noise = await StoryboardTests.ConfirmedFactAsync(member, product, "Chống ồn chủ động");
        var side = await StoryboardTests.UploadPhotoAsync(member, product);

        var response = await RegenerateAsync(member, variant, 2, position: 3);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var third = await ReadAsync<StoryboardResponse>(response);
        Assert.Equal($"{StoryboardTests.Storyboards(variant)}/3", response.Headers.Location?.OriginalString);
        Assert.Equal(3, third.Version);
        var facts = third.Scenes[2];
        Assert.Equal((3, SceneLayout.Facts, Technique.TextAnimation, 8000), (facts.Position, facts.Layout, facts.Technique, facts.DurationMs));
        Assert.Equal([Battery, "Chống ồn chủ động"], facts.OnScreenText);
        Assert.Equal($"{Battery}. Chống ồn chủ động.", facts.NarrationText);
        Assert.Equal([new SceneFactResponse(battery.Id, Battery), new SceneFactResponse(noise.Id, "Chống ồn chủ động")], facts.Facts);
        Assert.Equal([side.Id], facts.AssetIds);
        Assert.False(facts.ManuallyEdited);
        // The others are as the version before has them, the reveal still in a person's words and showing the photo it showed.
        Assert.Equal(Json(second.Scenes.Where((_, index) => index != 2)), Json(third.Scenes.Where((_, index) => index != 2)));
        Assert.True(third.Scenes[1].ManuallyEdited);
        Assert.Equal([front.Id], third.Scenes[1].AssetIds);
        Assert.Equal(Json(second), Json(await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/2")));

        // Regenerating a Scene a person rewrote gives it back to the planner.
        var fourth = await ReadAsync<StoryboardResponse>(await RegenerateAsync(member, variant, 3, position: 2));
        Assert.Equal(["Lumo 500"], fourth.Scenes[1].OnScreenText);
        Assert.Equal(4000, fourth.Scenes[1].DurationMs);
        Assert.False(fourth.Scenes[1].ManuallyEdited);
        Assert.Equal(Json(third.Scenes.Where((_, index) => index != 1)), Json(fourth.Scenes.Where((_, index) => index != 1)));
    }

    [Fact]
    public async Task Regenerating_a_Scene_that_would_come_out_the_same_is_refused_and_makes_no_version()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var first = await StoryboardTests.GeneratedAsync(member, variant);

        // Nothing the planner writes from has changed, and the planner writes the same from the same.
        var refused = new List<HttpResponseMessage>();
        foreach (var scene in first.Scenes) refused.Add(await RegenerateAsync(member, variant, 1, scene.Position));

        Assert.All(refused, response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        Assert.Contains("would come out exactly as it is", await ReasonAsync(refused[0]));
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);

        // Once there is something new to write from, the Scene that shows it is planned again.
        await StoryboardTests.ConfirmedFactAsync(member, product, "Chống ồn chủ động");
        var facts = first.Scenes.Single(scene => scene.Layout == SceneLayout.Facts).Position;
        Assert.Equal(HttpStatusCode.Created, (await RegenerateAsync(member, variant, 1, facts)).StatusCode);
    }

    [Fact]
    public async Task Regenerating_a_Scene_fails_with_the_reason_when_it_cannot_be_planned()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        await StoryboardTests.GeneratedAsync(member, variant);
        (await FactTests.WithdrawAsync(member, product, battery.Id)).EnsureSuccessStatusCode();

        var facts = await RegenerateAsync(member, variant, 1, position: 3);
        // The Hook needs no Fact, but the version it would be part of still rests on the one withdrawn.
        var hook = await RegenerateAsync(member, variant, 1, position: 1);

        Assert.Contains("no Confirmed Fact", await ReasonAsync(facts));
        Assert.Contains("Scene 3 uses a Fact that is not Confirmed", await ReasonAsync(hook));
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
    }

    [Fact]
    public async Task Edits_are_only_made_under_the_version_the_Variant_and_the_Project_they_belong_to()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var other = await StoryboardTests.ReadyVariantAsync(member);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        var edit = new StoryboardEditRequest(Changing(first, new(2, OnScreenText: ["Lumo"])));
        var underOtherProject = $"{VariantTests.Variants(other.ProjectId)}/{variant.Id}/storyboards/1";
        using var stranger = app.NewBrowser();

        HttpResponseMessage[] notFound =
        [
            await member.PostAsync($"{StoryboardTests.Storyboards(variant)}/2/edits", edit),
            await member.PostAsync($"{StoryboardTests.Storyboards(other)}/1/edits", edit),
            await member.PostAsync($"{underOtherProject}/edits", edit),
            await RegenerateAsync(member, variant, 2, position: 1),
            await RegenerateAsync(member, other, 1, position: 1),
            await member.PostAsync($"{underOtherProject}/scenes/1/regenerate", new { }),
            await RegenerateAsync(member, variant, 1, position: 5),
            await RegenerateAsync(member, variant, 1, position: 0),
        ];
        HttpResponseMessage[] unauthorized =
        [
            await stranger.PostAsync($"{StoryboardTests.Storyboards(variant)}/1/edits", edit),
            await RegenerateAsync(stranger, variant, 1, position: 1),
        ];

        Assert.All(notFound, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.All(unauthorized, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(1, (await StoryboardTests.ListAsync(member, variant)).Total);
        Assert.Equal(0, (await StoryboardTests.ListAsync(member, other)).Total);
    }

    /// <summary>An edit that names every Scene of the version in its own place and changes none of them.</summary>
    internal static SceneEditRequest[] Unchanged(StoryboardResponse storyboard) =>
        [.. storyboard.Scenes.Select(scene => new SceneEditRequest(scene.Position))];

    /// <summary>An edit that names every Scene of the version in its own place, with these changes made to those they name.</summary>
    internal static SceneEditRequest[] Changing(StoryboardResponse storyboard, SceneEditRequest change, SceneEditRequest? another = null) =>
        [.. storyboard.Scenes.Select(scene =>
            new[] { change, another }.SingleOrDefault(changed => changed?.Position == scene.Position) ?? new SceneEditRequest(scene.Position))];

    internal static Task<HttpResponseMessage> EditAsync(Browser member, VariantResponse variant, int version, SceneEditRequest[] scenes) =>
        member.PostAsync($"{StoryboardTests.Storyboards(variant)}/{version}/edits", new StoryboardEditRequest(scenes));

    internal static async Task<StoryboardResponse> EditedAsync(Browser member, VariantResponse variant, int version, SceneEditRequest[] scenes)
    {
        var response = await EditAsync(member, variant, version, scenes);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancellation));
        return await ReadAsync<StoryboardResponse>(response);
    }

    internal static Task<HttpResponseMessage> RegenerateAsync(Browser member, VariantResponse variant, int version, int position) =>
        member.PostAsync($"{StoryboardTests.Storyboards(variant)}/{version}/scenes/{position}/regenerate", new { });

    private async Task<Browser> SignedInToNewOrganizationAsync() =>
        await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

    // Why no version was made, as the member is told.
    private static async Task<string> ReasonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await ReadAsync<ProblemDetails>(response)).Detail ?? "";
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;

    // When a version was created is left out: the database keeps a time less finely than the answer to making it gives it.
    private static string Json(StoryboardResponse storyboard) => JsonSerializer.Serialize(storyboard with { CreatedAt = default }, AffiVideoApp.Json);

    private static string Json(SceneResponse scene) => JsonSerializer.Serialize(scene, AffiVideoApp.Json);

    private static string Json(IEnumerable<SceneResponse> scenes) => JsonSerializer.Serialize(scenes, AffiVideoApp.Json);
}
