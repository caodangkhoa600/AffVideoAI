using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using SkiaSharp;
using static AffiVideo.Api.Tests.HybridStoryboardTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Paid generation as a member meets it at Storyboard review: the estimate, the
/// confirmation, and the Still of a generated backdrop, made by the fake image provider.
/// </summary>
public sealed class StillGenerationTests(AffiVideoApp app)
{
    // What a Still by the fake provider is taken to cost in these tests.
    private const decimal Rate = 0.04m;

    [Fact]
    public async Task The_estimate_names_the_Still_of_each_generated_Scene_and_what_it_will_cost_against_the_ceiling()
    {
        await using var api = WithFakeImages(app, Rate);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        Assert.True(estimate.Available);
        Assert.Null(estimate.Unavailable);
        var still = Assert.Single(estimate.Stills);
        Assert.Equal(
            (1, Technique.GeneratedBackdrop, storyboard.Scenes[0].Description, 0.04m),
            (still.ScenePosition, still.Technique, still.Description, still.EstimatedAmount));
        Assert.Equal((0.04m, "USD"), (estimate.Total, estimate.Currency));
        // Nothing has been generated for the Project yet, and its ceiling is the Organization's: $5 unless an Owner changed it.
        Assert.Equal((0m, 5m), (estimate.Spent, estimate.Ceiling));
        // What will make the Still, said before anything is made.
        Assert.Equal(ImageProvider.Fake, estimate.ImageProvider);
    }

    [Fact]
    public async Task With_no_image_provider_generation_says_it_is_unavailable()
    {
        using var member = await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));

        Assert.False(estimate.Available);
        Assert.Contains("no image provider", estimate.Unavailable);
        Assert.Empty(estimate.Stills);
        Assert.Equal(0m, estimate.Total);
        Assert.Equal(ImageProvider.None, estimate.ImageProvider);
    }

    [Fact]
    public async Task Nothing_is_generated_until_the_member_confirms_the_estimate_they_saw()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);

        var unconfirmed = await member.PostAsync(Generation(hybrid, 1), new { }, NewKey());
        // Not the estimate as it stands: the member confirmed some other amount.
        var another = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(0.03m), NewKey());
        var unkeyed = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(Rate));

        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Contains("0.04", (await another.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail);
        Assert.Equal(HttpStatusCode.BadRequest, unkeyed.StatusCode);
        Assert.Equal(0, (await StillsAsync(member, hybrid)).Total);
        Assert.Equal(0m, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    [Fact]
    public async Task Confirming_the_estimate_queues_one_Still_for_the_Scene_which_every_Variant_of_the_Project_then_has()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        // The same creative template for the same Product and audience: its Hook Scene asks for the same picture.
        var sibling = await VariantTests.AddAsync(
            member, hybrid.ProjectId, new VariantRequest(CreativeTemplate.ProductShowcase, "Uống nước ấm cả ngày", RenderMode.Hybrid));
        await GeneratedAsync(member, sibling);
        var key = NewKey();

        var response = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(Rate), key);
        var repeated = await member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(Rate), key);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var still = Assert.Single((await response.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!.Stills);
        Assert.Equal(
            ((Guid?)hybrid.ProjectId, Technique.GeneratedBackdrop, storyboard.Scenes[0].Description, StillState.Queued, 0),
            (still.ProjectId, still.Technique, still.Description, still.State, still.Attempt));
        Assert.Equal((Rate, "USD"), (still.EstimatedAmount, still.Currency));
        // Said before anything is made: the fake will make it.
        Assert.Equal((ImageProvider.Fake, true), (still.Provider, still.Fake));
        Assert.Null(still.Failure);

        // The same click again is the same Still, not another.
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(still.Id, Assert.Single((await repeated.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!.Stills).Id);
        Assert.Equal(still.Id, Assert.Single((await StillsAsync(member, hybrid)).Items).Id);
        Assert.Equal(still.Id, (await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}")).Id);

        // The Scene has its Still, so there is nothing left to confirm, for this Variant or the other.
        foreach (var variant in new[] { hybrid, sibling })
        {
            var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, 1));
            Assert.Empty(estimate.Stills);
            Assert.Equal((0m, Rate), (estimate.Total, estimate.Spent));
            var has = Assert.Single(estimate.SceneStills);
            Assert.Equal((1, still.Id), (has.ScenePosition, has.Still.Id));
            var nothing = await member.PostAsync(Generation(variant, 1), new ConfirmGenerationRequest(0m), NewKey());
            Assert.Equal(HttpStatusCode.Conflict, nothing.StatusCode);
        }
    }

    [Fact]
    public async Task The_worker_has_the_Still_made_and_it_is_then_ready_for_review_with_its_picture_and_its_cost_and_labelled_fake()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        var queued = await ConfirmedAsync(member, hybrid, 1);
        var before = await member.GetAsync($"/api/v1/stills/{queued.Id}/content");
        var worker = await stack.StartWorkerAsync(FakeImageWorker);

        var still = await EndedAsync(member, queued, worker);

        Assert.Equal((StillState.ReadyForReview, 1), (still.State, still.Attempt));
        Assert.Null(still.Failure);
        // What made it is in the data: the fake, by name.
        Assert.Equal((ImageProvider.Fake, true, "fake-still"), (still.Provider, still.Fake, still.Model));

        // There is no picture until it is made, and then it is a vertical one the size of the video.
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        var content = await member.GetAsync($"/api/v1/stills/{still.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        using var picture = SKBitmap.Decode(await content.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal((1080, 1920), (picture.Width, picture.Height));

        // Its Production cost record: the estimate it was confirmed at, by what made it.
        Assert.Null(queued.Cost);
        Assert.NotNull(still.Cost);
        Assert.Equal((Rate, "USD", "1"), (still.Cost.EstimatedAmount, still.Cost.Currency, still.Cost.RatesVersion));
        Assert.Equal((ImageProvider.Fake, "fake-still"), (still.Cost.Provider, still.Cost.Model));
        Assert.Equal(Rate, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    [Fact]
    public async Task A_member_approves_a_Still_or_asks_for_another_which_is_a_new_paid_generation()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        var queued = await ConfirmedAsync(member, hybrid, 1);
        // Not made yet: there is nothing to approve, and nothing to want another of.
        var tooSoon = await member.PostAsync($"/api/v1/stills/{queued.Id}/approve", new { });
        var tooSoonForAnother = await member.PostAsync($"/api/v1/stills/{queued.Id}/another", new AnotherStillRequest(Rate), NewKey());
        var worker = await stack.StartWorkerAsync(FakeImageWorker);
        var first = await EndedAsync(member, queued, worker);
        var key = NewKey();
        var before = DateTimeOffset.UtcNow;
        // The estimate says what another would cost, and another is only made for the amount the member saw.
        var anotherAmount = (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).AnotherStillAmount;
        var unconfirmed = await member.PostAsync($"/api/v1/stills/{first.Id}/another", new { }, NewKey());
        var forLess = await member.PostAsync($"/api/v1/stills/{first.Id}/another", new AnotherStillRequest(0.03m), NewKey());
        var untouched = await member.GetAsync<StillResponse>($"/api/v1/stills/{first.Id}");

        var asked = await member.PostAsync($"/api/v1/stills/{first.Id}/another", new AnotherStillRequest(Rate), key);
        var repeated = await member.PostAsync($"/api/v1/stills/{first.Id}/another", new AnotherStillRequest(Rate), key);

        Assert.Equal(Rate, anotherAmount);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, forLess.StatusCode);
        Assert.Contains("0.04 USD", (await forLess.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail);
        Assert.Equal(StillState.ReadyForReview, untouched.State);
        Assert.All([tooSoon, tooSoonForAnother], refused => Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode));
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var another = (await asked.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!;
        Assert.NotEqual(first.Id, another.Id);
        Assert.Equal((first.ProjectId, first.Technique, first.Description, Rate), (another.ProjectId, another.Technique, another.Description, another.EstimatedAmount));
        // The same click again is the same Still.
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(another.Id, (await repeated.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!.Id);
        var second = await EndedAsync(member, another, worker);
        Assert.Equal(StillState.ReadyForReview, second.State);

        // The first is rejected and kept, with its picture and what it cost.
        var rejected = await member.GetAsync<StillResponse>($"/api/v1/stills/{first.Id}");
        Assert.Equal(StillState.Rejected, rejected.State);
        Assert.Equal(Rate, rejected.Cost?.EstimatedAmount);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/stills/{first.Id}/content")).StatusCode);
        Assert.Equal([second.Id, first.Id], (await StillsAsync(member, hybrid)).Items.Select(still => still.Id));
        // Both were paid for, and the Scene now has the second.
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        Assert.Equal(2 * Rate, estimate.Spent);
        Assert.Equal(second.Id, Assert.Single(estimate.SceneStills).Still.Id);
        Assert.Empty(estimate.Stills);

        var approval = await member.PostAsync($"/api/v1/stills/{second.Id}/approve", new { });
        var again = await member.PostAsync($"/api/v1/stills/{second.Id}/approve", new { });
        var ofRejected = await member.PostAsync($"/api/v1/stills/{first.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approved = (await approval.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!;
        Assert.Equal(StillState.Approved, approved.State);
        Assert.NotNull(approved.ApprovedByMemberId);
        Assert.InRange(approved.ApprovedAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        // Approved once, and only what waits for the member can be.
        Assert.All([again, ofRejected], refused => Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode));
        Assert.Equal(StillState.Approved, (await member.GetAsync<StillResponse>($"/api/v1/stills/{second.Id}")).State);
    }

    [Fact]
    public async Task Approving_a_Still_makes_the_next_Storyboard_version_of_each_Variant_that_describes_its_picture_and_that_version_refers_to_it()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        var first = await GeneratedAsync(member, hybrid);
        // The same creative template for the same Product and audience: its Hook Scene describes the same picture.
        var sibling = await VariantTests.AddAsync(
            member, hybrid.ProjectId, new VariantRequest(CreativeTemplate.ProductShowcase, "Uống nước ấm cả ngày", RenderMode.Hybrid));
        await GeneratedAsync(member, sibling);
        // Another creative template describes another picture, and Product Lock has none.
        var luxury = await VariantTests.AddAsync(
            member, hybrid.ProjectId, new VariantRequest(CreativeTemplate.LuxuryCinematic, "Uống nước ấm cả ngày", RenderMode.Hybrid));
        await GeneratedAsync(member, luxury);
        var locked = await VariantTests.AddAsync(member, hybrid.ProjectId);
        await GeneratedAsync(member, locked);
        var worker = await stack.StartWorkerAsync(FakeImageWorker);
        var still = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);
        var beforeApproval = await ListAsync(member, hybrid);

        (await member.PostAsync($"/api/v1/stills/{still.Id}/approve", new { })).EnsureSuccessStatusCode();

        // A Still that is only made is nobody's choice yet: no version refers to it.
        Assert.Equal(1, beforeApproval.Total);
        Assert.Null(beforeApproval.Items[0].Scenes[0].StillId);
        foreach (var variant in new[] { hybrid, sibling })
        {
            var versions = (await ListAsync(member, variant)).Items;
            Assert.Equal([2, 1], versions.Select(version => version.Version));
            // The version that was there stays as it was: a version never changes.
            Assert.All(versions[1].Scenes, scene => Assert.Null(scene.StillId));
            Assert.Equal(still.Id, versions[0].Scenes[0].StillId);
            Assert.All(versions[0].Scenes.Skip(1), scene => Assert.Null(scene.StillId));
            // Nothing else about the Scenes differs.
            Assert.Equal(
                versions[1].Scenes.Select(scene => scene with { StillId = null }), versions[0].Scenes.Select(scene => scene with { StillId = null }),
                SameScene);
        }
        Assert.Equal(first.Scenes[0].Description, (await ListAsync(member, hybrid)).Items[0].Scenes[0].Description);
        Assert.Equal(1, (await ListAsync(member, luxury)).Total);
        Assert.Equal(1, (await ListAsync(member, locked)).Total);
    }

    [Fact]
    public async Task A_version_made_once_a_Still_is_approved_refers_to_it_for_as_long_as_its_Scene_describes_that_picture_and_the_Still_stays_approved()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        var worker = await stack.StartWorkerAsync(FakeImageWorker);
        var still = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);
        (await member.PostAsync($"/api/v1/stills/{still.Id}/approve", new { })).EnsureSuccessStatusCode();
        var second = (await ListAsync(member, hybrid)).Items[0];

        // Planned again, and planned for another Variant of the Project: the picture is there already.
        var planned = await GeneratedAsync(member, hybrid);
        var sibling = await VariantTests.AddAsync(
            member, hybrid.ProjectId, new VariantRequest(CreativeTemplate.ProductShowcase, "Uống nước ấm cả ngày", RenderMode.Hybrid));
        var ofSibling = await GeneratedAsync(member, sibling);
        // An edit of anything but the description leaves the picture as it is.
        var reworded = await StoryboardEditTests.EditedAsync(
            member, hybrid, 2, StoryboardEditTests.Changing(second, new SceneEditRequest(2, OnScreenText: ["Lumo 500 mới về"])));
        // Another picture is described: the Still is not of it.
        var described = await StoryboardEditTests.EditedAsync(
            member, hybrid, 2, StoryboardEditTests.Changing(second, new SceneEditRequest(1, Description: "Bàn gỗ sồi bên cửa sổ")));

        Assert.Equal((2, 3, 1, 4, 5), (second.Version, planned.Version, ofSibling.Version, reworded.Version, described.Version));
        Assert.All([second, planned, ofSibling, reworded], version => Assert.Equal(still.Id, version.Scenes[0].StillId));
        Assert.Null(described.Scenes[0].StillId);
        Assert.Empty((await member.GetAsync<GenerationEstimateResponse>(Generation(sibling, 1))).Stills);
        // An edit that changes nothing still makes no version.
        Assert.Equal(HttpStatusCode.Conflict, (await StoryboardEditTests.EditAsync(member, hybrid, 2, StoryboardEditTests.Unchanged(second))).StatusCode);

        // Another is asked for in its place: the Still is no longer approved, so a version made now refers to none.
        (await member.PostAsync($"/api/v1/stills/{still.Id}/another", new AnotherStillRequest(Rate), NewKey())).EnsureSuccessStatusCode();
        var afterRejection = await StoryboardEditTests.EditedAsync(
            member, hybrid, 2, StoryboardEditTests.Changing(second, new SceneEditRequest(2, OnScreenText: ["Lumo 500 đã về"])));

        Assert.Null(afterRejection.Scenes[0].StillId);
        // The versions that referred to it still do.
        Assert.Equal(still.Id, (await member.GetAsync<StoryboardResponse>($"{Storyboards(hybrid)}/2")).Scenes[0].StillId);
    }

    private static readonly IEqualityComparer<SceneResponse> SameScene = EqualityComparer<SceneResponse>.Create(
        (a, b) => System.Text.Json.JsonSerializer.Serialize(a, AffiVideoApp.Json) == System.Text.Json.JsonSerializer.Serialize(b, AffiVideoApp.Json));

    [Fact]
    public async Task A_generation_that_would_take_the_Project_past_its_ceiling_is_refused_with_the_reason_and_nothing_is_generated()
    {
        // Two dollars a Still, under the ceiling of five: two can be paid for, and a third cannot.
        await using var stack = await app.NewStackAsync(FakeImages(rate: 2m));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        var worker = await stack.StartWorkerAsync(FakeImageWorker);
        var first = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);
        var asked = await member.PostAsync($"/api/v1/stills/{first.Id}/another", new AnotherStillRequest(2m), NewKey());
        var second = await EndedAsync(member, (await asked.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!, worker);

        var third = await member.PostAsync($"/api/v1/stills/{second.Id}/another", new AnotherStillRequest(2m), NewKey());
        // A Scene that describes another picture needs a Still of its own, which the Project cannot pay for either.
        await StoryboardEditTests.EditedAsync(
            member, hybrid, 1, StoryboardEditTests.Changing(storyboard, new SceneEditRequest(1, Description: "Bàn gỗ sồi bên cửa sổ")));
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 2));
        var confirmed = await member.PostAsync(Generation(hybrid, 2), new ConfirmGenerationRequest(estimate.Total), NewKey());

        foreach (var refused in new[] { third, confirmed })
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var reason = (await refused.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail;
            Assert.Contains("6.00 USD", reason);
            Assert.Contains("ceiling of 5.00 USD", reason);
        }
        // The estimate had said what it would cost, and where the Project stood.
        Assert.Equal((2m, 4m, 5m), (estimate.Total, estimate.Spent, estimate.Ceiling));
        // Nothing was queued, nothing more was spent, and the Still the member has is still theirs to approve.
        Assert.Equal([second.Id, first.Id], (await StillsAsync(member, hybrid)).Items.Select(still => still.Id));
        Assert.Equal(StillState.ReadyForReview, (await member.GetAsync<StillResponse>($"/api/v1/stills/{second.Id}")).State);
        Assert.Equal(4m, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 2))).Spent);
    }

    [Fact]
    public async Task A_technical_failure_that_was_not_billed_is_tried_again_without_the_member()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        // The provider fails once, charging nothing, and then works.
        var worker = await stack.StartWorkerAsync(FakeImageWorker, ("Generation__Fake__TechnicalFailures", "1"), ShortWait);

        var still = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);

        Assert.Equal((StillState.ReadyForReview, 2), (still.State, still.Attempt));
        Assert.Null(still.Failure);
        // One Still, paid for once.
        Assert.Equal(Rate, still.Cost?.EstimatedAmount);
        Assert.Equal(Rate, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
        Assert.Equal(1, (await StillsAsync(member, hybrid)).Total);
    }

    [Theory]
    // The provider will not make the picture. Asking again for the same would be refused again.
    [InlineData("Generation__Fake__Refuses", "true", "false", StillFailureKind.Refused, 1, false)]
    // It failed and charged for it all the same: trying again would be paying again without being asked.
    [InlineData("Generation__Fake__TechnicalFailures", "9", "true", StillFailureKind.Technical, 1, true)]
    // It fails every time, charging nothing: it is tried as often as a job is, here twice, and no more.
    [InlineData("Generation__Fake__TechnicalFailures", "9", "false", StillFailureKind.Technical, 2, false)]
    public async Task A_Still_that_is_refused_or_keeps_failing_ends_failed_with_a_reason_and_is_not_tried_again(
        string setting, string value, string billsFailures, StillFailureKind kind, int attempts, bool billed)
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        var worker = await stack.StartWorkerAsync(
            FakeImageWorker, (setting, value), ("Generation__Fake__BillsFailures", billsFailures), ShortWait, ("RenderQueue__MaxAttempts", "2"));

        var still = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);
        // Long enough for another attempt to have been made, had one been coming.
        await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);
        still = await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}");

        Assert.Equal((StillState.Failed, attempts), (still.State, still.Attempt));
        Assert.NotNull(still.Failure);
        Assert.Equal((kind, billed), (still.Failure.Kind, still.Failure.Billed));
        // A reason the member can act on: what the provider said when it refused, or to ask again.
        Assert.Contains(kind == StillFailureKind.Refused ? "The fake image provider refuses every picture" : "Ask for it again", still.Failure.Message);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/stills/{still.Id}/content")).StatusCode);
        // It has its record all the same: what it cost, which is nothing unless the provider charged.
        var spent = billed ? Rate : 0m;
        Assert.Equal(spent, still.Cost?.EstimatedAmount);
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        Assert.Equal(spent, estimate.Spent);
        // The Scene has no Still, so it is the member's to ask for again.
        Assert.Empty(estimate.SceneStills);
        Assert.Equal(1, Assert.Single(estimate.Stills).ScenePosition);
    }

    [Fact]
    public async Task A_Still_whose_worker_stops_fails_when_its_lease_runs_out_and_is_not_made_again_without_the_member()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        (string, string)[] shortLease = [("RenderQueue__LeaseDuration", "00:00:03"), ("RenderQueue__LeaseRenewalInterval", "00:00:01")];
        // The provider takes longer than the test lets it have.
        var slow = await stack.StartWorkerAsync([FakeImageWorker, ("Generation__Fake__DelaySeconds", "120"), .. shortLease]);
        var queued = await ConfirmedAsync(member, hybrid, 1);
        var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        StillResponse taken;
        while ((taken = await member.GetAsync<StillResponse>($"/api/v1/stills/{queued.Id}")).State != StillState.Generating)
        {
            Assert.True(DateTimeOffset.UtcNow < until, $"The Still was {taken.State}, not being made, after 30 seconds.\n{await RenderStack.LogAsync(slow)}");
            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
        }

        await RenderStack.KillAsync(slow);
        var worker = await stack.StartWorkerAsync([FakeImageWorker, ShortWait, .. shortLease]);
        var still = await EndedAsync(member, taken, worker);
        // Long enough for another attempt to have been made, had one been coming.
        await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);
        still = await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}");

        // Nobody knows whether the provider charged for what the lost worker asked of it, so it is
        // not asked again without the member, and the attempt is taken to have been charged for.
        Assert.Equal((StillState.Failed, 1), (still.State, still.Attempt));
        Assert.NotNull(still.Failure);
        Assert.Equal((StillFailureKind.WorkerLost, true), (still.Failure.Kind, still.Failure.Billed));
        Assert.Equal(Rate, still.Cost?.EstimatedAmount);
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1));
        Assert.Equal(Rate, estimate.Spent);
        // The Scene has no Still: it is the member's to ask for again.
        Assert.Equal(1, Assert.Single(estimate.Stills).ScenePosition);
        Assert.Equal(1, (await StillsAsync(member, hybrid)).Total);
    }

    [Fact]
    public async Task A_picture_the_provider_made_but_the_worker_could_not_keep_is_paid_for_and_is_not_made_again_without_the_member()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);
        // The provider makes the picture, and the worker has nowhere to put it.
        var worker = await stack.StartWorkerAsync(FakeImageWorker, ("Storage__Bucket", "no-such-bucket"), ShortWait);

        var still = await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), worker);
        // Long enough for another attempt to have been made, had one been coming.
        await Task.Delay(TimeSpan.FromSeconds(3), Cancellation);
        still = await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}");

        Assert.Equal((StillState.Failed, 1), (still.State, still.Attempt));
        Assert.NotNull(still.Failure);
        Assert.Equal((StillFailureKind.Technical, true), (still.Failure.Kind, still.Failure.Billed));
        Assert.Equal(Rate, still.Cost?.EstimatedAmount);
        Assert.Equal(Rate, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    [Fact]
    public async Task Confirmations_that_arrive_at_the_same_moment_pay_for_a_Scenes_Still_once()
    {
        await using var stack = await app.NewStackAsync(FakeImages(Rate));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        await GeneratedAsync(member, hybrid);

        // Eight different clicks, as from eight tabs: each has a key of its own.
        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            member.PostAsync(Generation(hybrid, 1), new ConfirmGenerationRequest(Rate), NewKey())));

        Assert.Equal(1, answers.Count(answer => answer.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(7, answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await StillsAsync(member, hybrid)).Total);
        Assert.Equal(Rate, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    [Fact]
    public async Task Confirmations_that_arrive_at_the_same_moment_cannot_take_the_Project_past_its_ceiling_between_them()
    {
        // Two dollars a Still under a ceiling of five: two can be paid for, whichever two, and no more.
        await using var stack = await app.NewStackAsync(FakeImages(rate: 2m));
        using var member = await stack.NewMemberAsync();
        var hybrid = await HybridVariantAsync(member);
        var storyboard = await GeneratedAsync(member, hybrid);
        // Six versions, each describing a picture of its own, so each needs a Still of its own.
        for (var version = 1; version <= 5; version++)
        {
            storyboard = await StoryboardEditTests.EditedAsync(
                member, hybrid, version, StoryboardEditTests.Changing(storyboard, new SceneEditRequest(1, Description: $"Bàn gỗ số {version}")));
        }

        var answers = await Task.WhenAll(Enumerable.Range(1, 6).Select(version =>
            member.PostAsync(Generation(hybrid, version), new ConfirmGenerationRequest(2m), NewKey())));

        Assert.Equal(2, answers.Count(answer => answer.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(4, answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(2, (await StillsAsync(member, hybrid)).Total);
        Assert.Equal(4m, (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, 1))).Spent);
    }

    // The wait before a second attempt, short enough for a test to sit through.
    private static readonly (string Name, string Value) ShortWait = ("RenderQueue__RetryBaseDelay", "00:00:01");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // A worker that has the fake image provider, as its environment names the setting.
    internal static readonly (string Name, string Value) FakeImageWorker = ("Generation__ImageProvider", "Fake");

    /// <summary>The one Still that confirming the estimate of this Storyboard version queues.</summary>
    internal static async Task<StillResponse> ConfirmedAsync(Browser member, VariantResponse variant, int version)
    {
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, version));
        var response = await member.PostAsync(Generation(variant, version), new ConfirmGenerationRequest(estimate.Total), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!.Stills);
    }

    /// <summary>The Still once no worker has anything left to do with it: made, or failed.</summary>
    internal static async Task<StillResponse> EndedAsync(Browser member, StillResponse still, IContainer worker)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (true)
        {
            still = await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}");
            if (still.State is not (StillState.Queued or StillState.Generating)) return still;
            Assert.True(DateTimeOffset.UtcNow < until, $"The Still was still {still.State} after two minutes.\n{await RenderStack.LogAsync(worker)}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancellation);
        }
    }

    internal static (string Name, string Value) NewKey() => ("Idempotency-Key", Guid.NewGuid().ToString());

    internal static Task<PagedResponse<StillResponse>> StillsAsync(Browser member, VariantResponse variant) =>
        member.GetAsync<PagedResponse<StillResponse>>($"{ProjectTests.Projects}/{variant.ProjectId}/stills");

    /// <summary>Settings of an API that has the fake image provider, with a Still by it taken to cost this much.</summary>
    internal static Dictionary<string, string> FakeImages(decimal rate) => new()
    {
        ["Generation:ImageProvider"] = "Fake",
        ["ProductionCost:FakeStill"] = rate.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    internal static string Generation(VariantResponse variant, int version) => $"{Storyboards(variant)}/{version}/generation";

    /// <summary>The API with the fake image provider, and a Still by it taken to cost this much.</summary>
    internal static WebApplicationFactory<Program> WithFakeImages(AffiVideoApp app, decimal rate) => app.With(FakeImages(rate));
}
