using System.Net;
using System.Text.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The production cost records a render leaves, as a member sees them over HTTP:
/// on the Rendered Video and added up for its Product. Each test that needs rates
/// of its own, or a worker to kill, has a queue of its own and starts its workers.
/// </summary>
public sealed class ProductionCostTests(AffiVideoApp app)
{
    private const string RatesVersion = "test-2026-10";

    // A lease short enough to watch run out, as in the tests of the queue.
    private static readonly (string, string)[] ShortLease =
    [
        ("RenderQueue__LeaseDuration", "00:00:08"),
        ("RenderQueue__LeaseRenewalInterval", "00:00:01"),
        ("RenderQueue__RetryBaseDelay", "00:00:01"),
    ];

    // An attempt costs 250 dong, however long it takes: what the tests below add up.
    private static readonly (string, string)[] ByTheAttempt =
    [
        ("ProductionCost__RatesVersion", RatesVersion),
        ("ProductionCost__Currency", "VND"),
        ("ProductionCost__LocalPerAttempt", "250"),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_render_with_no_rates_configured_is_recorded_as_an_estimate_of_nothing()
    {
        var rendered = await RenderTests.RenderedAsync(app);

        var cost = rendered.Video.ProductionCost;
        var attempt = Assert.Single(cost.Attempts);
        Assert.Equal((1, RenderAttemptOutcome.Completed, RenderProvider.Local), (attempt.Attempt, attempt.Outcome, attempt.Provider));
        Assert.Equal((0m, "USD", "1"), (attempt.EstimatedAmount, attempt.Currency, attempt.RatesVersion));
        Assert.True(attempt.DurationMs > 0, "The attempt is recorded as having taken no time.");
        // Nothing, in a currency: a total there is, not one that is missing.
        Assert.Equal([new EstimatedAmountResponse(0m, "USD")], cost.EstimatedTotals);
    }

    [Fact]
    public async Task A_render_writes_a_record_estimated_at_the_configured_rates_which_its_Rendered_Video_and_its_Product_show()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var worker = await stack.StartWorkerAsync([.. ByTheAttempt, ("ProductionCost__LocalPerMinute", "1200")]);

        var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, renderable.Variant, 1));

        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure}\n{await RenderStack.LogAsync(worker)}");
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
        var attempt = Assert.Single(video.ProductionCost.Attempts);
        Assert.Equal((1, RenderAttemptOutcome.Completed, RenderProvider.Local), (attempt.Attempt, attempt.Outcome, attempt.Provider));
        // What the attempt rendered: the Scenes of the Storyboard version, by Technique.
        Assert.Equal(
            renderable.Storyboard.Scenes.GroupBy(scene => scene.Technique).OrderBy(made => made.Key)
                .Select(made => new TechniqueCountResponse(made.Key, made.Count())),
            attempt.TechniqueCounts);
        Assert.Equal(renderable.Storyboard.Scenes.Count, attempt.TechniqueCounts.Sum(count => count.Scenes));
        // How long the worker had the job: some of the time since it was queued, and not more.
        Assert.InRange(attempt.DurationMs, 1, (long)(job.UpdatedAt - job.CreatedAt).TotalMilliseconds + 1);
        // 250 for the attempt and 1200 for each minute it took, in the currency and under the name the worker was given.
        Assert.Equal(250m + Math.Round(1200m * attempt.DurationMs / 60_000m, 6, MidpointRounding.AwayFromZero), attempt.EstimatedAmount);
        Assert.Equal(("VND", RatesVersion), (attempt.Currency, attempt.RatesVersion));
        Assert.Equal([new EstimatedAmountResponse(attempt.EstimatedAmount, "VND")], video.ProductionCost.EstimatedTotals);

        // The library says the same of the video, and the Product adds up what was rendered for it.
        var listed = Assert.Single((await member.GetAsync<PagedResponse<RenderedVideoResponse>>("/api/v1/rendered-videos")).Items);
        Assert.Equal(
            JsonSerializer.Serialize(video.ProductionCost, AffiVideoApp.Json), JsonSerializer.Serialize(listed.ProductionCost, AffiVideoApp.Json));
        var ofProduct = await ProductCostAsync(member, renderable.ProductId);
        Assert.Equal(video.ProductionCost.EstimatedTotals, ofProduct.EstimatedTotals);
        Assert.Equal((1, 0), (ofProduct.Attempts, ofProduct.FailedAttempts));

        // Rendered again, the Product has cost two attempts, and each video only its own.
        var again = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, renderable.Variant, 1));
        Assert.True(again.State == RenderJobState.Completed, $"The second render ended {again.State}: {again.Failure}\n{await RenderStack.LogAsync(worker)}");
        var second = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{again.RenderedVideoId}");
        var both = await ProductCostAsync(member, renderable.ProductId);
        Assert.Single(second.ProductionCost.Attempts);
        Assert.Equal((2, 0), (both.Attempts, both.FailedAttempts));
        Assert.Equal(
            [new EstimatedAmountResponse(attempt.EstimatedAmount + second.ProductionCost.Attempts[0].EstimatedAmount, "VND")],
            both.EstimatedTotals);

        // The records are kept: deleting the videos, and then the Project with its jobs, takes nothing off the Product.
        foreach (var made in new[] { video, second })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"/api/v1/rendered-videos/{made.Id}")).StatusCode);
        }
        Assert.Equal(both, await ProductCostAsync(member, renderable.ProductId), SameCost);
        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"{ProjectTests.Projects}/{renderable.Variant.ProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/render-jobs/{job.Id}")).StatusCode);
        Assert.Equal(both, await ProductCostAsync(member, renderable.ProductId), SameCost);
    }

    [Fact]
    public async Task Every_failed_attempt_writes_a_record_whether_the_job_is_tried_again_or_not()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var unrenderable = await RenderableAsync(member);
        (await member.DeleteAsync($"{ProductAssetTests.Assets(unrenderable.ProductId)}/{unrenderable.PhotoId}")).EnsureSuccessStatusCode();
        var untouched = await StoryboardTests.NewProductAsync(member);
        // A worker with nothing to draw with: every attempt at a render fails in the same place.
        await stack.StartWorkerAsync(
        [
            .. ByTheAttempt,
            ("Rendering__RemotionDirectory", "/nowhere"),
            ("Rendering__PollInterval", "00:00:00.200"),
            ("RenderQueue__MaxAttempts", "3"),
            ("RenderQueue__RetryBaseDelay", "00:00:01"),
        ]);

        var tried = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, renderable.Variant, 1));
        var refused = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, unrenderable.Variant, 1));

        // Tried three times, and each attempt cost what an attempt costs.
        Assert.Equal((RenderJobState.Failed, 3), (tried.State, tried.Attempt));
        var ofTried = await ProductCostAsync(member, renderable.ProductId);
        Assert.Equal((3, 3), (ofTried.Attempts, ofTried.FailedAttempts));
        Assert.Equal([new EstimatedAmountResponse(750m, "VND")], ofTried.EstimatedTotals);
        // A Storyboard that cannot be rendered is tried once, and that once is recorded.
        Assert.Equal((RenderJobState.Failed, 1, RenderFailureCategory.InvalidInput), (refused.State, refused.Attempt, refused.Failure!.Category));
        var ofRefused = await ProductCostAsync(member, unrenderable.ProductId);
        Assert.Equal((1, 1), (ofRefused.Attempts, ofRefused.FailedAttempts));
        Assert.Equal([new EstimatedAmountResponse(250m, "VND")], ofRefused.EstimatedTotals);
        // A Product nothing was rendered for has no total: not a total of nothing.
        var ofUntouched = await ProductCostAsync(member, untouched);
        Assert.Empty(ofUntouched.EstimatedTotals);
        Assert.Equal((0, 0), (ofUntouched.Attempts, ofUntouched.FailedAttempts));
    }

    [Fact]
    public async Task A_Rendered_Video_shows_the_attempt_a_dead_worker_lost_before_the_one_that_made_it()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var first = await stack.StartWorkerAsync([.. ShortLease, .. ByTheAttempt]);
        var job = await RenderTests.SubmittedAsync(member, renderable.Variant, 1);
        await UntilRenderingAsync(member, job);

        await RenderStack.KillAsync(first);
        var second = await stack.StartWorkerAsync([.. ShortLease, .. ByTheAttempt]);
        var ended = await RenderTests.EndedAsync(member, job);

        Assert.True(ended.State == RenderJobState.Completed, $"The render ended {ended.State}: {ended.Failure}\n{await RenderStack.LogAsync(second)}");
        Assert.Equal(2, ended.Attempt);
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{ended.RenderedVideoId}");
        Assert.Equal(
            [(1, RenderAttemptOutcome.Failed), (2, RenderAttemptOutcome.Completed)],
            video.ProductionCost.Attempts.Select(attempt => (attempt.Attempt, attempt.Outcome)));
        // Nobody was there to say when the first attempt ended: it is taken to have lasted until its lease ran out.
        var lost = video.ProductionCost.Attempts[0];
        Assert.InRange(lost.DurationMs, 8_000, (long)(ended.UpdatedAt - ended.CreatedAt).TotalMilliseconds);
        Assert.Equal(video.ProductionCost.Attempts[1].TechniqueCounts, lost.TechniqueCounts);
        Assert.All(video.ProductionCost.Attempts, attempt => Assert.Equal((250m, "VND", RatesVersion), (attempt.EstimatedAmount, attempt.Currency, attempt.RatesVersion)));
        // The video cost both attempts, and so did its Product.
        Assert.Equal([new EstimatedAmountResponse(500m, "VND")], video.ProductionCost.EstimatedTotals);
        var ofProduct = await ProductCostAsync(member, renderable.ProductId);
        Assert.Equal([new EstimatedAmountResponse(500m, "VND")], ofProduct.EstimatedTotals);
        Assert.Equal((2, 1), (ofProduct.Attempts, ofProduct.FailedAttempts));
    }

    [Fact]
    public async Task The_last_attempt_of_a_job_whose_workers_keep_dying_is_recorded_when_the_job_is_failed()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var first = await stack.StartWorkerAsync([.. ShortLease, .. ByTheAttempt]);
        var job = await RenderTests.SubmittedAsync(member, renderable.Variant, 1);
        await UntilRenderingAsync(member, job);

        await RenderStack.KillAsync(first);
        // To this worker a job is tried once, and this one has been.
        await stack.StartWorkerAsync([.. ShortLease, .. ByTheAttempt, ("RenderQueue__MaxAttempts", "1")]);
        var ended = await RenderTests.EndedAsync(member, job);

        Assert.Equal((RenderJobState.Failed, 1, RenderFailureCategory.WorkerLost), (ended.State, ended.Attempt, ended.Failure!.Category));
        var ofProduct = await ProductCostAsync(member, renderable.ProductId);
        Assert.Equal((1, 1), (ofProduct.Attempts, ofProduct.FailedAttempts));
        Assert.Equal([new EstimatedAmountResponse(250m, "VND")], ofProduct.EstimatedTotals);
    }

    [Fact]
    public async Task The_production_cost_of_a_Product_that_does_not_exist_is_not_found_and_is_refused_to_someone_not_signed_in()
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        using var stranger = app.NewBrowser();
        var product = await StoryboardTests.NewProductAsync(member);

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(ProductCost(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync(ProductCost(product))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(ProductCost(product))).StatusCode);
    }

    internal static string ProductCost(Guid productId) => $"/api/v1/products/{productId}/production-cost";

    private static Task<ProductProductionCostResponse> ProductCostAsync(Browser member, Guid productId) =>
        member.GetAsync<ProductProductionCostResponse>(ProductCost(productId));

    /// <summary>A Storyboard version that renders without the cut-out model: its one photo came already cut out.</summary>
    private static async Task<Renderable> RenderableAsync(Browser member)
    {
        var product = await StoryboardTests.NewProductAsync(member);
        var photo = await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, RenderTests.Bottle(onBackdrop: false));
        await StoryboardTests.ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15);
        return new Renderable(product, photo.Id, variant, await StoryboardTests.GeneratedAsync(member, variant));
    }

    private static async Task UntilRenderingAsync(Browser member, RenderJobResponse job)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(4);
        while ((await member.GetAsync<RenderJobResponse>($"/api/v1/render-jobs/{job.Id}")).State != RenderJobState.Rendering)
        {
            Assert.True(DateTimeOffset.UtcNow < until, "The job never came to be rendered.");
            await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        }
    }

    // The totals are lists, which a record compares by reference.
    private static readonly IEqualityComparer<ProductProductionCostResponse> SameCost =
        EqualityComparer<ProductProductionCostResponse>.Create((a, b) =>
            a!.Attempts == b!.Attempts && a.FailedAttempts == b.FailedAttempts && a.EstimatedTotals.SequenceEqual(b.EstimatedTotals));

    private sealed record Renderable(Guid ProductId, Guid PhotoId, VariantResponse Variant, StoryboardResponse Storyboard);
}
