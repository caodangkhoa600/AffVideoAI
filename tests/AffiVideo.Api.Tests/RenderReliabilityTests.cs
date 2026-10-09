using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

/// <summary>
/// What a render job survives, as a member sees it over HTTP: a second click, a
/// cancellation, a worker that dies, a failure that may pass. Each test has a queue
/// of its own and starts the workers that serve it.
/// </summary>
public sealed class RenderReliabilityTests(AffiVideoApp app)
{
    // A lease short enough to watch run out, renewed often enough that a busy machine does not lose it.
    private static readonly (string, string)[] ShortLease =
    [
        ("RenderQueue__LeaseDuration", "00:00:08"),
        ("RenderQueue__LeaseRenewalInterval", "00:00:01"),
        ("RenderQueue__RetryBaseDelay", "00:00:01"),
    ];

    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(60);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submitting_a_render_again_with_the_same_idempotency_key_returns_the_job_it_already_queued()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);

        var first = await RenderTests.SubmitAsync(member, renderable.Renders, "click-1");
        var again = await RenderTests.SubmitAsync(member, renderable.Renders, "click-1");
        // Two clicks that reach the API at the same moment.
        var together = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => RenderTests.SubmitAsync(member, renderable.Renders, "click-2")));
        var other = await RenderTests.SubmitAsync(member, renderable.Renders, "click-3");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var job = await ReadAsync(first);
        Assert.Equal(job.Id, (await ReadAsync(again)).Id);
        var atOnce = new List<RenderJobResponse>();
        foreach (var response in together) atOnce.Add(await ReadAsync(response));
        Assert.Single(together, response => response.StatusCode == HttpStatusCode.Accepted);
        Assert.Single(atOnce.Select(queued => queued.Id).Distinct());
        Assert.NotEqual(job.Id, atOnce[0].Id);
        // Another key is another click: three jobs in all, however many requests there were.
        Assert.Equal(HttpStatusCode.Accepted, other.StatusCode);
        var listed = await member.GetAsync<PagedResponse<RenderJobResponse>>(renderable.Renders);
        Assert.Equal(3, listed.Total);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a-key-that-is-longer-than-one-hundred-characters-which-is-more-than-any-client-has-a-reason-to-send-us")]
    public async Task A_render_submitted_without_a_usable_idempotency_key_is_refused(string? key)
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);

        var response = key is null
            ? await member.PostAsync(renderable.Renders, new { })
            : await RenderTests.SubmitAsync(member, renderable.Renders, key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await member.GetAsync<PagedResponse<RenderJobResponse>>(renderable.Renders)).Total);
    }

    [Fact]
    public async Task A_member_can_cancel_a_queued_job_and_no_worker_then_takes_it()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        using var memberOfAnother = await stack.NewMemberAsync();
        using var stranger = stack.NewBrowser();
        var renderable = await RenderableAsync(member);
        var job = await SubmittedAsync(member, renderable);

        var byAnother = await CancelAsync(memberOfAnother, job);
        var byStranger = await CancelAsync(stranger, job);
        var unknown = await member.PostAsync($"/api/v1/render-jobs/{Guid.NewGuid()}/cancel", new { });
        Assert.Equal(RenderJobState.Queued, (await JobAsync(member, job)).State);
        var cancelled = await CancelAsync(member, job);
        var again = await CancelAsync(member, job);

        Assert.Equal(HttpStatusCode.NotFound, byAnother.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, byStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(RenderJobState.Cancelled, (await ReadAsync(cancelled)).State);
        // Cancelling what is cancelled changes nothing.
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // A worker that comes by afterwards takes the job queued behind it, and leaves this one.
        var behind = await SubmittedAsync(member, await UnrenderableAsync(member));
        await stack.StartWorkerAsync();
        Assert.Equal(RenderJobState.Failed, (await RenderTests.EndedAsync(member, behind)).State);
        var left = await JobAsync(member, job);
        Assert.Equal((RenderJobState.Cancelled, 0), (left.State, left.Attempt));
        Assert.Null(left.Failure);
        Assert.Null(left.RenderedVideoId);
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_the_programs_drawing_it_and_removes_its_temporary_files()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var worker = await stack.StartWorkerAsync(ShortLease);
        var job = await SubmittedAsync(member, renderable);
        await UntilAsync(
            async () => (await JobAsync(member, job)).State == RenderJobState.Rendering && await DrawingAsync(worker),
            "The job never came to be drawn.", TimeSpan.FromMinutes(4));
        Assert.Contains(job.Id.ToString("N"), await WorkFoldersAsync(worker));

        var cancelled = await CancelAsync(member, job);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(RenderJobState.Cancelled, (await ReadAsync(cancelled)).State);
        await UntilAsync(
            async () => !await DrawingAsync(worker) && !(await WorkFoldersAsync(worker)).Contains(job.Id.ToString("N")),
            "Remotion or FFmpeg was still running, or the job's folder was still there.", Soon);

        // The worker itself goes on: it renders the next job, which by then cannot be cancelled.
        var next = await RenderTests.EndedAsync(member, await SubmittedAsync(member, renderable));
        Assert.True(next.State == RenderJobState.Completed, $"The next render ended {next.State}: {next.Failure}\n{await RenderStack.LogAsync(worker)}");
        var tooLate = await CancelAsync(member, next);
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        Assert.Contains("already completed", (await tooLate.Content.ReadFromJsonAsync<ProblemDetails>(AffiVideoApp.Json, Cancellation))!.Detail);
        // And the cancelled job stayed cancelled, with nothing made.
        var left = await JobAsync(member, job);
        Assert.Equal((RenderJobState.Cancelled, 1), (left.State, left.Attempt));
        Assert.Null(left.RenderedVideoId);
        Assert.Null(left.Failure);
        var listed = await member.GetAsync<PagedResponse<RenderJobResponse>>(renderable.Renders);
        Assert.Single(listed.Items, listedJob => listedJob.RenderedVideoId is not null);
    }

    [Fact]
    public async Task A_worker_renders_no_more_jobs_at_once_than_it_is_configured_to()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var worker = await stack.StartWorkerAsync([.. ShortLease, ("Rendering__JobsAtOnce", "2")]);
        var jobs = new List<RenderJobResponse>();
        for (var index = 0; index < 3; index++) jobs.Add(await SubmittedAsync(member, renderable));

        async Task<List<RenderJobResponse>> NowAsync()
        {
            var now = new List<RenderJobResponse>();
            foreach (var job in jobs) now.Add(await JobAsync(member, job));
            Assert.InRange(now.Count(job => job.State.IsRunning()), 0, 2);
            return now;
        }

        // Two are taken, and for as long as they are being rendered the third waits.
        await UntilAsync(
            async () => (await NowAsync()).Count(job => job.State == RenderJobState.Rendering) == 2,
            "Two jobs were never being rendered at once.", TimeSpan.FromMinutes(4));
        var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTimeOffset.UtcNow < until)
        {
            var now = await NowAsync();
            if (now.Any(job => job.State.HasEnded())) break;
            Assert.Equal(2, now.Count(job => job.State.IsRunning()));
            var waiting = Assert.Single(now, job => job.State == RenderJobState.Queued);
            Assert.True(waiting.Attempt == 0, $"The job that waits has been taken {waiting.Attempt} times.\n{await RenderStack.LogAsync(worker)}");
            // Oldest first: the one that waits is the one submitted last.
            Assert.Equal(jobs[2].Id, waiting.Id);
            await Task.Delay(TimeSpan.FromMilliseconds(300), Cancellation);
        }

        foreach (var job in jobs) await CancelAsync(member, job);
    }

    [Fact]
    public async Task Two_workers_take_each_job_once_and_a_job_longer_than_its_lease_stays_with_its_worker()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var unrenderable = await UnrenderableAsync(member);
        var workers = await Task.WhenAll(stack.StartWorkerAsync(ShortLease), stack.StartWorkerAsync(ShortLease));

        // One job that takes several leases to render, and many that end as soon as they are taken.
        var render = await SubmittedAsync(member, renderable);
        var quick = new List<RenderJobResponse>();
        foreach (var response in await Task.WhenAll(
            Enumerable.Range(0, 24).Select(_ => RenderTests.SubmitAsync(member, unrenderable.Renders))))
        {
            quick.Add(await ReadAsync(response));
        }

        foreach (var job in quick)
        {
            var ended = await RenderTests.EndedAsync(member, job);
            Assert.Equal((RenderJobState.Failed, 1), (ended.State, ended.Attempt));
        }
        var rendered = await RenderTests.EndedAsync(member, render);
        Assert.True(
            rendered.State == RenderJobState.Completed,
            $"The render ended {rendered.State}: {rendered.Failure}\n{await RenderStack.LogAsync(workers[0])}\n{await RenderStack.LogAsync(workers[1])}");
        // Its lease ran out several times over while it rendered, and nobody took it: it was renewed.
        Assert.True(rendered.UpdatedAt - rendered.CreatedAt > TimeSpan.FromSeconds(8), "The render was over before its first lease.");
        Assert.Equal(1, rendered.Attempt);
        // Both workers were serving the queue: each took some of the jobs.
        foreach (var worker in workers)
        {
            var (output, errors) = await worker.GetLogsAsync(ct: Cancellation);
            Assert.Contains("Rendering job", output + errors);
        }
    }

    [Fact]
    public async Task A_job_whose_worker_dies_goes_back_to_the_queue_and_is_completed_by_another_worker()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var first = await stack.StartWorkerAsync(ShortLease);
        var job = await SubmittedAsync(member, renderable);
        await UntilAsync(
            async () => (await JobAsync(member, job)).State == RenderJobState.Rendering,
            "The job never came to be rendered.", TimeSpan.FromMinutes(4));

        await RenderStack.KillAsync(first);
        var left = await JobAsync(member, job);
        var second = await stack.StartWorkerAsync(ShortLease);
        var states = new List<RenderJobState> { left.State };
        var ended = await RenderTests.EndedAsync(member, job, polled =>
        {
            if (states[^1] != polled.State) states.Add(polled.State);
            return Task.CompletedTask;
        });

        // Nobody was there to say the worker had died: the job was left as it was.
        Assert.Equal((RenderJobState.Rendering, 1), (left.State, left.Attempt));
        Assert.True(ended.State == RenderJobState.Completed, $"The render ended {ended.State}: {ended.Failure}\n{await RenderStack.LogAsync(second)}");
        Assert.Equal(2, ended.Attempt);
        // It went back to the queue and through its stages again, from the first.
        Assert.Equal(RenderJobState.Rendering, states[0]);
        Assert.Contains(states[1], new[] { RenderJobState.Queued, RenderJobState.Validating, RenderJobState.Planning, RenderJobState.GeneratingAssets });
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{ended.RenderedVideoId}");
        Assert.Equal(job.Id, video.RenderJobId);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/rendered-videos/{video.Id}/content")).StatusCode);
    }

    [Fact]
    public async Task A_job_whose_workers_die_as_often_as_a_job_is_tried_is_failed_and_says_the_worker_stopped()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var first = await stack.StartWorkerAsync(ShortLease);
        var job = await SubmittedAsync(member, renderable);
        await UntilAsync(
            async () => (await JobAsync(member, job)).State == RenderJobState.Rendering,
            "The job never came to be rendered.", TimeSpan.FromMinutes(4));

        await RenderStack.KillAsync(first);
        // To this worker a job is tried once, and this one has been.
        await stack.StartWorkerAsync([.. ShortLease, ("RenderQueue__MaxAttempts", "1")]);
        var ended = await RenderTests.EndedAsync(member, job);

        Assert.Equal((RenderJobState.Failed, 1), (ended.State, ended.Attempt));
        Assert.NotNull(ended.Failure);
        Assert.Equal(RenderJobState.Rendering, ended.Failure.Stage);
        Assert.Equal(RenderFailureCategory.WorkerLost, ended.Failure.Category);
        Assert.Equal(
            "The video could not be rendered: the worker stopped before it had finished, and the job had been tried once. Try rendering again.",
            ended.Failure.Message);
        Assert.StartsWith("The lease of attempt 1 ran out at ", ended.Failure.Detail);
        Assert.Null(ended.RenderedVideoId);
    }

    [Fact]
    public async Task A_job_that_keeps_failing_is_tried_three_times_each_after_twice_the_wait_and_then_failed_with_what_went_wrong()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        // A worker with nothing to draw with: every attempt fails in the same place.
        var worker = await stack.StartWorkerAsync(
            ("Rendering__RemotionDirectory", "/nowhere"),
            ("Rendering__PollInterval", "00:00:00.200"),
            ("RenderQueue__MaxAttempts", "3"),
            ("RenderQueue__RetryBaseDelay", "00:00:02"));

        var seen = new List<RenderJobResponse>();
        var ended = await RenderTests.EndedAsync(member, await SubmittedAsync(member, renderable), polled =>
        {
            seen.Add(polled);
            return Task.CompletedTask;
        });

        Assert.Equal((RenderJobState.Failed, 3), (ended.State, ended.Attempt));
        Assert.NotNull(ended.Failure);
        Assert.Equal(RenderJobState.GeneratingAssets, ended.Failure.Stage);
        Assert.Equal(RenderFailureCategory.Internal, ended.Failure.Category);
        Assert.Equal(
            "The video could not be rendered: something went wrong while preparing the Product's photos. Try rendering again.",
            ended.Failure.Message);
        Assert.Contains("There is no Remotion bundle at /nowhere", ended.Failure.Detail);
        Assert.Null(ended.RetryAt);

        // Between attempts the job is queued, saying when it may be taken again: after 2 seconds, then after 4.
        var afterFirst = seen.First(job => job is { State: RenderJobState.Queued, Attempt: 1 });
        var afterSecond = seen.First(job => job is { State: RenderJobState.Queued, Attempt: 2 });
        Assert.Null(afterFirst.Failure);
        Assert.Equal(TimeSpan.FromSeconds(2), Rounded(afterFirst.RetryAt!.Value - afterFirst.UpdatedAt));
        Assert.Equal(TimeSpan.FromSeconds(4), Rounded(afterSecond.RetryAt!.Value - afterSecond.UpdatedAt));
        // And it is not taken before then.
        Assert.All(seen.Where(job => job.Attempt == 2), job => Assert.True(job.UpdatedAt >= afterFirst.RetryAt));
        Assert.All(seen.Where(job => job.Attempt == 3), job => Assert.True(job.UpdatedAt >= afterSecond.RetryAt));
        // A job that has not been tried has nothing to wait for.
        Assert.All(seen.Where(job => job.Attempt == 0), job => Assert.Null(job.RetryAt));
        Assert.DoesNotContain(ended.Id.ToString("N"), await WorkFoldersAsync(worker));
    }

    [Fact]
    public async Task A_job_whose_drawing_takes_too_long_is_stopped_and_fails_saying_so()
    {
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        var renderable = await RenderableAsync(member);
        var worker = await stack.StartWorkerAsync(("Rendering__ProcessTimeout", "00:00:02"), ("RenderQueue__MaxAttempts", "1"));

        var ended = await RenderTests.EndedAsync(member, await SubmittedAsync(member, renderable));

        Assert.Equal((RenderJobState.Failed, 1), (ended.State, ended.Attempt));
        Assert.NotNull(ended.Failure);
        Assert.Equal(RenderJobState.Rendering, ended.Failure.Stage);
        Assert.Equal(RenderFailureCategory.Timeout, ended.Failure.Category);
        Assert.Equal(
            "The video could not be rendered: it took too long while drawing the Scenes. Try rendering again.", ended.Failure.Message);
        Assert.Contains("TimeoutException", ended.Failure.Detail);
        await UntilAsync(
            async () => !await DrawingAsync(worker) && !(await WorkFoldersAsync(worker)).Contains(ended.Id.ToString("N")),
            "Remotion or FFmpeg was still running, or the job's folder was still there.", Soon);
    }

    /// <summary>A Storyboard version that renders without the cut-out model: its one photo came already cut out.</summary>
    private static async Task<Renderable> RenderableAsync(Browser member)
    {
        var product = await StoryboardTests.NewProductAsync(member);
        var photo = await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, RenderTests.Bottle(onBackdrop: false));
        await StoryboardTests.ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15);
        var storyboard = await StoryboardTests.GeneratedAsync(member, variant);
        return new Renderable(product, photo.Id, RenderTests.Renders(variant, storyboard.Version));
    }

    /// <summary>A Storyboard version whose photo has since been removed: a job for it fails as soon as a worker takes it.</summary>
    private static async Task<Renderable> UnrenderableAsync(Browser member)
    {
        var renderable = await RenderableAsync(member);
        (await member.DeleteAsync($"{ProductAssetTests.Assets(renderable.ProductId)}/{renderable.PhotoId}")).EnsureSuccessStatusCode();
        return renderable;
    }

    private static async Task<RenderJobResponse> SubmittedAsync(Browser member, Renderable renderable)
    {
        var response = await RenderTests.SubmitAsync(member, renderable.Renders);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await ReadAsync(response);
    }

    private static Task<RenderJobResponse> JobAsync(Browser member, RenderJobResponse job) =>
        member.GetAsync<RenderJobResponse>($"/api/v1/render-jobs/{job.Id}");

    private static Task<HttpResponseMessage> CancelAsync(Browser member, RenderJobResponse job) =>
        member.PostAsync($"/api/v1/render-jobs/{job.Id}/cancel", new { });

    // Whether Remotion, its browser or FFmpeg is running in the worker's container. The
    // brackets keep the search from finding itself.
    private static async Task<bool> DrawingAsync(IContainer worker)
    {
        var found = await worker.ExecAsync(
            ["sh", "-c", "grep -l -e '[r]emotion' -e '[f]fmpeg' /proc/[0-9]*/cmdline 2>/dev/null | wc -l"], Cancellation);
        return int.Parse(found.Stdout.Trim()) > 0;
    }

    // The folders of the jobs the worker has temporary files for, each named after its job.
    private static async Task<string> WorkFoldersAsync(IContainer worker) =>
        (await worker.ExecAsync(["sh", "-c", "ls -A /tmp/affivideo-render 2>/dev/null; true"], Cancellation)).Stdout;

    private static async Task UntilAsync(Func<Task<bool>> so, string otherwise, TimeSpan within)
    {
        var until = DateTimeOffset.UtcNow + within;
        while (!await so())
        {
            Assert.True(DateTimeOffset.UtcNow < until, otherwise);
            await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
        }
    }

    // The database keeps a time to the microsecond.
    private static TimeSpan Rounded(TimeSpan span) => TimeSpan.FromMilliseconds(Math.Round(span.TotalMilliseconds));

    private static async Task<RenderJobResponse> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<RenderJobResponse>(AffiVideoApp.Json, Cancellation))!;

    private sealed record Renderable(Guid ProductId, Guid PhotoId, string Renders);
}
