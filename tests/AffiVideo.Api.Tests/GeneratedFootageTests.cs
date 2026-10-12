using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc;
using static AffiVideo.Api.Tests.StillGenerationTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Generated footage as a member meets it at Storyboard review: a Scene with no Product
/// in it, planned by the creative template, whose approved Still becomes a Clip under the
/// one confirmation. The Still and the Clip are the fake providers'.
/// </summary>
public sealed class GeneratedFootageTests(AffiVideoApp app)
{
    // What a Still, and a second of a Clip, by the fake providers are taken to cost in these tests.
    private const decimal StillRate = 0.04m;
    private const decimal ClipRate = 0.10m;

    [Fact]
    public async Task With_a_video_provider_a_Hybrid_Variant_has_Scenes_of_generated_footage_described_by_its_creative_template()
    {
        await using var api = app.With(FakeFootage(StillRate, ClipRate));
        using var member = await HybridStoryboardTests.SignedInToNewOrganizationAsync(app, api);
        var product = (await ProductTests.CreateAsync(member, ProductTests.Valid(category: "Bình giữ nhiệt"))).Id;
        await UploadPhotoAsync(member, product);
        var fact = await ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { Audience = "Dân văn phòng 25-35 tuổi" });
        var showcase = await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        var problem = await VariantTests.AddAsync(
            member, project.Id, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?", RenderMode.Hybrid));
        var locked = await VariantTests.AddAsync(member, project.Id, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?"));

        var storyboard = await GeneratedAsync(member, showcase);
        var ofProblem = await GeneratedAsync(member, problem);
        var ofLocked = await GeneratedAsync(member, locked);

        Assert.Equal(
            [Technique.GeneratedBackdrop, Technique.ImageMotion, Technique.GeneratedFootage, Technique.ImageMotion],
            storyboard.Scenes.Select(scene => scene.Technique));
        var facts = storyboard.Scenes[2];
        // The Product's category and the audience are placed in the template's description whole.
        Assert.Contains("Bình giữ nhiệt", facts.Description);
        Assert.Contains("Dân văn phòng 25-35 tuổi", facts.Description);
        // What it asks for is nobody real and nothing of the Product.
        Assert.Contains("invented adult", facts.Description);
        Assert.Contains("No product", facts.Description);
        // The footage is seen under the Facts, and rests on them.
        Assert.Equal([fact.Id], facts.Facts.Select(used => used.FactId));
        Assert.Null(facts.ClipId);
        Assert.Null(facts.StillId);

        // A problem with no Product in the frame is footage too, and a video has no more than two generated Scenes.
        Assert.Equal(
            [Technique.GeneratedFootage, Technique.ImageMotion, Technique.GeneratedFootage, Technique.ImageMotion],
            ofProblem.Scenes.Select(scene => scene.Technique));
        Assert.Contains("invented adult", ofProblem.Scenes[0].Description);
        // Product Lock generates nothing, whatever there is to generate with.
        Assert.All(ofLocked.Scenes, scene => Assert.False(scene.Technique is Technique.GeneratedBackdrop or Technique.GeneratedFootage));
    }

    [Fact]
    public async Task With_an_image_provider_and_no_video_provider_only_the_backdrop_is_generated()
    {
        await using var api = HybridStoryboardTests.WithFakeImages(app);
        using var member = await HybridStoryboardTests.SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);

        var storyboard = await GeneratedAsync(member, hybrid);
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        Assert.Equal(
            [Technique.GeneratedBackdrop, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            storyboard.Scenes.Select(scene => scene.Technique));
        Assert.Equal(VideoProvider.None, estimate.VideoProvider);
        Assert.Empty(estimate.Clips);
    }

    [Fact]
    public async Task The_estimate_names_the_Still_and_the_Clip_of_generated_footage_and_a_Clip_costs_by_the_length_of_its_Scene()
    {
        await using var api = app.With(FakeFootage(StillRate, ClipRate));
        using var member = await HybridStoryboardTests.SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        var footage = storyboard.Scenes[2];

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        Assert.True(estimate.Available);
        // A Still for the backdrop, and one for the footage to start from.
        Assert.Equal(
            [(1, Technique.GeneratedBackdrop, StillRate), (3, Technique.GeneratedFootage, StillRate)],
            estimate.Stills.Select(still => (still.ScenePosition, still.Technique, still.EstimatedAmount)));
        var clip = Assert.Single(estimate.Clips);
        var clipAmount = ClipRate * footage.DurationMs / 1000m;
        Assert.Equal((3, footage.Description, footage.DurationMs, clipAmount), (clip.ScenePosition, clip.Description, clip.DurationMs, clip.EstimatedAmount));
        // One total, for the one confirmation.
        Assert.Equal(2 * StillRate + clipAmount, estimate.Total);
        Assert.Equal((ImageProvider.Fake, VideoProvider.Fake), (estimate.ImageProvider, estimate.VideoProvider));
        Assert.Equal((0m, 5m), (estimate.Spent, estimate.Ceiling));
        Assert.Empty(estimate.SceneClips);
    }

    [Fact]
    public async Task A_Clip_is_made_only_from_its_approved_Still_at_its_Scenes_length_with_no_sound_and_a_member_watches_and_approves_it()
    {
        await using var stack = await app.NewStackAsync(FakeFootage(StillRate, ClipRate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        // The same creative template for the same Product, audience and Facts: its Facts Scene asks for the same footage.
        var sibling = await VariantTests.AddAsync(
            member, hybrid.ProjectId, new VariantRequest(CreativeTemplate.ProductShowcase, "Uống nước ấm cả ngày", RenderMode.Hybrid));
        await GeneratedAsync(member, sibling);
        var footage = storyboard.Scenes[2];
        var clipAmount = ClipRate * footage.DurationMs / 1000m;
        var worker = await stack.StartWorkerAsync(FakeFootageWorker);
        var key = NewKey();

        // One confirmation, of the one total, covers the Stills and the Clip.
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        var response = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(estimate.Total), key);
        var repeated = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(estimate.Total), key);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var confirmed = (await response.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!;
        Assert.Equal(2, confirmed.Stills.Count);
        var waiting = Assert.Single(confirmed.Clips);
        Assert.Equal(
            (ClipState.WaitingForStill, null, footage.Description, footage.DurationMs, clipAmount),
            (waiting.State, waiting.StillId, waiting.Description, waiting.DurationMs, waiting.EstimatedAmount));
        // Said before anything is made: the fake will make it.
        Assert.Equal((VideoProvider.Fake, true), (waiting.Provider, waiting.Fake));
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(
            waiting.Id, Assert.Single((await repeated.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!.Clips).Id);
        // What was confirmed counts against the ceiling at once, and there is nothing left to confirm.
        estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        Assert.Equal((2 * StillRate + clipAmount, 0m), (estimate.Spent, estimate.Total));
        Assert.Empty(estimate.Clips);
        Assert.Equal((3, waiting.Id), Assert.Single(estimate.SceneClips.Select(has => (has.ScenePosition, has.Clip.Id))));

        // The Stills are made. The Clip is not: its Still is not approved, and no worker takes it.
        var stills = new List<StillResponse>();
        foreach (var queued in confirmed.Stills) stills.Add(await EndedAsync(member, queued, worker));
        Assert.All(stills, still => Assert.Equal(StillState.ReadyForReview, still.State));
        var ofFootage = stills.Single(still => still.Technique == Technique.GeneratedFootage);
        await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);
        var stillWaiting = await member.GetAsync<ClipResponse>($"/api/v1/clips/{waiting.Id}");
        Assert.Equal((ClipState.WaitingForStill, 0), (stillWaiting.State, stillWaiting.Attempt));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/clips/{waiting.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsync($"/api/v1/clips/{waiting.Id}/approve", new { })).StatusCode);

        // Approving the Still is what starts the Clip, with no second confirmation.
        (await member.PostAsync($"/api/v1/stills/{ofFootage.Id}/approve", new { })).EnsureSuccessStatusCode();
        var clip = await ClipEndedAsync(member, waiting, worker);

        Assert.True(clip.State == ClipState.ReadyForReview, $"The Clip ended {clip.State}: {clip.Failure?.Message}\n{await RenderStack.LogAsync(worker)}");
        Assert.Equal((ofFootage.Id, 1, footage.DurationMs), (clip.StillId, clip.Attempt, clip.DurationMs));
        // What made it is in the data: the fake, by name.
        Assert.Equal((VideoProvider.Fake, true, "fake-clip"), (clip.Provider, clip.Fake, clip.Model));
        // Its Production cost record: the estimate it was confirmed at, by what made it, on the Clip alone.
        Assert.Null(waiting.Cost);
        Assert.NotNull(clip.Cost);
        Assert.Equal((clipAmount, "USD", "1"), (clip.Cost.EstimatedAmount, clip.Cost.Currency, clip.Cost.RatesVersion));
        Assert.Equal((VideoProvider.Fake, "fake-clip"), (clip.Cost.Provider, clip.Cost.Model));
        Assert.Equal(clip.Id, Assert.Single((await ClipsAsync(member, hybrid)).Items).Id);

        // The member watches it: a vertical video as long as its Scene, in which nothing is heard.
        var content = await member.GetAsync($"/api/v1/clips/{clip.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("video/mp4", content.Content.Headers.ContentType?.MediaType);
        await worker.CopyAsync(await content.Content.ReadAsByteArrayAsync(Cancellation), "/tmp/probe/clip.mp4", ct: Cancellation);
        var probe = await worker.ExecAsync(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", "/tmp/probe/clip.mp4"], Cancellation);
        Assert.Equal(0, probe.ExitCode);
        using var described = JsonDocument.Parse(probe.Stdout);
        var video = Assert.Single(described.RootElement.GetProperty("streams").EnumerateArray());
        Assert.Equal("video", video.GetProperty("codec_type").GetString());
        Assert.Equal((1080, 1920), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32()));
        var seconds = double.Parse(described.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, footage.DurationMs / 1000.0 - 0.05, footage.DurationMs / 1000.0 + 0.1);

        // The Scene has its Clip, so another Still would be paid for and shown nowhere.
        var another = await member.PostAsync($"/api/v1/stills/{ofFootage.Id}/another", new AnotherStillRequest(StillRate), NewKey());
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Contains("Clip", (await another.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail);

        // Approving the Clip makes the version that refers to it, for every Variant of the Project that describes that footage.
        var before = DateTimeOffset.UtcNow;
        var approval = await member.PostAsync($"/api/v1/clips/{clip.Id}/approve", new { });
        var again = await member.PostAsync($"/api/v1/clips/{clip.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approved = (await approval.Content.ReadFromJsonAsync<ClipResponse>(AffiVideoApp.Json, Cancellation))!;
        Assert.Equal(ClipState.Approved, approved.State);
        Assert.NotNull(approved.ApprovedByMemberId);
        Assert.InRange(approved.ApprovedAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        foreach (var variant in new[] { hybrid, sibling })
        {
            var newest = (await ListAsync(member, variant)).Items[0];
            Assert.Equal(2, newest.Version);
            Assert.Equal(((Guid?)clip.Id, (Guid?)ofFootage.Id), (newest.Scenes[2].ClipId, newest.Scenes[2].StillId));
            // The version it was made from is as it was.
            Assert.Null((await member.GetAsync<StoryboardResponse>($"{Storyboards(variant)}/1")).Scenes[2].ClipId);
            var left = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, 2));
            Assert.Empty(left.Clips);
            Assert.DoesNotContain(left.Stills, still => still.Technique == Technique.GeneratedFootage);
        }
        // Everything was paid for once: two Stills and one Clip.
        Assert.Equal(2 * StillRate + clipAmount, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 2))).Spent);
    }

    [Fact]
    public async Task A_Clip_that_would_take_the_Project_past_its_ceiling_is_refused_with_its_Still_and_nothing_is_generated()
    {
        // A dollar a second: the Facts Scene of a 20-second video lasts seven, which alone is past the ceiling of five.
        await using var api = app.With(FakeFootage(StillRate, clipPerSecond: 1m));
        using var member = await HybridStoryboardTests.SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        var response = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(estimate.Total), NewKey());

        Assert.True(estimate.Total > estimate.Ceiling);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ceiling of 5.00 USD", (await response.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail);
        Assert.Equal(0, (await StillsAsync(member, hybrid)).Total);
        Assert.Equal(0, (await ClipsAsync(member, hybrid)).Total);
        Assert.Equal(0m, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    [Fact]
    public async Task A_Clip_the_provider_refuses_fails_with_the_reason_costs_nothing_and_is_the_members_to_ask_for_again()
    {
        await using var stack = await app.NewStackAsync(FakeFootage(StillRate, ClipRate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        var worker = await stack.StartWorkerAsync([.. FakeFootageWorker, ("Generation__FakeVideo__Refuses", "true")]);
        var confirmed = await ConfirmedAsync(member, hybrid, 1);
        var ofFootage = await EndedAsync(member, confirmed.Stills.Single(still => still.Technique == Technique.GeneratedFootage), worker);
        (await member.PostAsync($"/api/v1/stills/{ofFootage.Id}/approve", new { })).EnsureSuccessStatusCode();

        var clip = await ClipEndedAsync(member, confirmed.Clips[0], worker);
        // Long enough for another attempt to have been made, had one been coming.
        await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);
        clip = await member.GetAsync<ClipResponse>($"/api/v1/clips/{clip.Id}");

        Assert.Equal((ClipState.Failed, 1), (clip.State, clip.Attempt));
        Assert.NotNull(clip.Failure);
        Assert.Equal((ClipFailureKind.Refused, false), (clip.Failure.Kind, clip.Failure.Billed));
        Assert.Contains("The fake video provider refuses every Clip", clip.Failure.Message);
        Assert.Contains("Change the Scene's description", clip.Failure.Message);
        // The record is kept, and says it cost nothing.
        Assert.Equal(0m, clip.Cost?.EstimatedAmount);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/clips/{clip.Id}/content")).StatusCode);
        // The Scene has no Clip. Its Still is approved, so only the Clip is offered again, at its Scene's length.
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        Assert.Empty(estimate.SceneClips);
        Assert.Equal((3, storyboard.Scenes[2].DurationMs), Assert.Single(estimate.Clips.Select(asked => (asked.ScenePosition, asked.DurationMs))));
        Assert.DoesNotContain(estimate.Stills, still => still.Technique == Technique.GeneratedFootage);
        // Only what was charged for is spent: the two Stills.
        Assert.Equal(2 * StillRate, estimate.Spent);
    }

    [Fact]
    public async Task A_Clip_that_fails_for_a_technical_reason_without_being_charged_for_is_tried_again_without_the_member()
    {
        await using var stack = await app.NewStackAsync(FakeFootage(StillRate, ClipRate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        var worker = await stack.StartWorkerAsync(
            [.. FakeFootageWorker, ("Generation__FakeVideo__TechnicalFailures", "1"), ("RenderQueue__RetryBaseDelay", "00:00:01")]);
        var confirmed = await ConfirmedAsync(member, hybrid, 1);
        var ofFootage = await EndedAsync(member, confirmed.Stills.Single(still => still.Technique == Technique.GeneratedFootage), worker);
        (await member.PostAsync($"/api/v1/stills/{ofFootage.Id}/approve", new { })).EnsureSuccessStatusCode();

        var clip = await ClipEndedAsync(member, confirmed.Clips[0], worker);

        // The first attempt failed and cost nothing, so the second was made with nobody asking.
        Assert.True(clip.State == ClipState.ReadyForReview, $"The Clip ended {clip.State}: {clip.Failure?.Message}\n{await RenderStack.LogAsync(worker)}");
        Assert.Equal(2, clip.Attempt);
        Assert.Null(clip.Failure);
        // One record, for the Clip that was made.
        Assert.Equal(ClipRate * storyboard.Scenes[2].DurationMs / 1000m, clip.Cost?.EstimatedAmount);
    }

    [Fact]
    public async Task A_Clip_whose_Still_was_not_made_waits_and_counts_against_the_ceiling_only_once_its_Still_is_asked_for_again()
    {
        await using var stack = await app.NewStackAsync(FakeFootage(StillRate, ClipRate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        var clipAmount = ClipRate * storyboard.Scenes[2].DurationMs / 1000m;
        // The image provider refuses every picture, and charges for none.
        var worker = await stack.StartWorkerAsync([.. FakeFootageWorker, ("Generation__Fake__Refuses", "true")]);
        var confirmed = await ConfirmedAsync(member, hybrid, 1);
        var confirmedAt = (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent;
        foreach (var queued in confirmed.Stills) Assert.Equal(StillState.Failed, (await EndedAsync(member, queued, worker)).State);
        await RenderStack.KillAsync(worker);

        var afterRefusal = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        var again = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(afterRefusal.Total), NewKey());
        var afterAsking = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        // While the Stills were on their way, everything confirmed counted.
        Assert.InRange(confirmedAt, clipAmount, 2 * StillRate + clipAmount);
        // No Still was made, so nothing will be made from one: nothing was charged, and nothing is counted.
        Assert.Equal(0m, afterRefusal.Spent);
        // The Clip waits as it was confirmed. Only the Stills are offered again.
        Assert.Equal((3, ClipState.WaitingForStill), Assert.Single(afterRefusal.SceneClips.Select(has => (has.ScenePosition, has.Clip.State))));
        Assert.Empty(afterRefusal.Clips);
        Assert.Equal((2, 2 * StillRate), (afterRefusal.Stills.Count, afterRefusal.Total));
        // With its Still asked for again the Clip is on its way again, and counted: once, and it is the same Clip.
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(2 * StillRate + clipAmount, afterAsking.Spent);
        Assert.Equal(confirmed.Clips[0].Id, Assert.Single((await ClipsAsync(member, hybrid)).Items).Id);
    }

    [Fact]
    public async Task A_Scene_whose_footage_shows_what_the_Product_does_cannot_be_given_text_that_cites_no_Fact()
    {
        await using var api = app.With(FakeFootage(StillRate, ClipRate));
        using var member = await HybridStoryboardTests.SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridStoryboardTests.HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);

        // A person's own words in place of the Fact: the footage would then say something of the Product that rests on nothing.
        var edited = await StoryboardEditTests.EditAsync(
            member, hybrid, 1, StoryboardEditTests.Changing(storyboard, new SceneEditRequest(3, OnScreenText: ["Nghe nhạc cả ngày"])));
        // Describing other footage leaves the Scene resting on its Fact.
        var described = await StoryboardEditTests.EditedAsync(
            member, hybrid, 1, StoryboardEditTests.Changing(storyboard, new SceneEditRequest(3, Description: "Một người uống nước mát giữa trưa hè")));

        Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
        var reason = (await edited.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail;
        Assert.Contains("Scene 3's generated picture shows what the Product does", reason);
        Assert.Contains("Confirmed Fact", reason);
        Assert.Equal("Một người uống nước mát giữa trưa hè", described.Scenes[2].Description);
        Assert.Equal(storyboard.Scenes[2].Facts, described.Scenes[2].Facts);
        Assert.Equal(2, (await ListAsync(member, hybrid)).Total);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A worker that has both fake providers, as its environment names the settings.</summary>
    internal static readonly (string Name, string Value)[] FakeFootageWorker =
        [("Generation__ImageProvider", "Fake"), ("Generation__VideoProvider", "Fake")];

    /// <summary>Settings of an API that has both fake providers, with a Still, and a second of a Clip, taken to cost this much.</summary>
    internal static Dictionary<string, string> FakeFootage(decimal stillRate, decimal clipPerSecond) => new()
    {
        ["Generation:ImageProvider"] = "Fake",
        ["Generation:VideoProvider"] = "Fake",
        ["ProductionCost:FakeStill"] = stillRate.ToString(CultureInfo.InvariantCulture),
        ["ProductionCost:FakeClipPerSecond"] = clipPerSecond.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Everything that confirming the estimate of this Storyboard version queues.</summary>
    internal static async Task<ConfirmedGenerationResponse> ConfirmedAsync(Browser member, VariantResponse variant, int version)
    {
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, version));
        var response = await member.PostAsync(Generation(variant, version), new ConfirmGenerationRequest(estimate.Total), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!;
    }

    /// <summary>The Clip once no worker has anything left to do with it: made, or failed. Its Still has to be approved, or it waits for ever.</summary>
    internal static async Task<ClipResponse> ClipEndedAsync(Browser member, ClipResponse clip, IContainer worker)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (true)
        {
            clip = await member.GetAsync<ClipResponse>($"/api/v1/clips/{clip.Id}");
            if (clip.State is not (ClipState.WaitingForStill or ClipState.Queued or ClipState.Generating)) return clip;
            Assert.True(DateTimeOffset.UtcNow < until, $"The Clip was still {clip.State} after two minutes.\n{await RenderStack.LogAsync(worker)}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancellation);
        }
    }

    internal static Task<PagedResponse<ClipResponse>> ClipsAsync(Browser member, VariantResponse variant) =>
        member.GetAsync<PagedResponse<ClipResponse>>($"{ProjectTests.Projects}/{variant.ProjectId}/clips");
}
