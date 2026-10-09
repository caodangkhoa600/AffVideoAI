using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Rendering as a member sees it: a job is queued over HTTP, the worker renders it
/// in its container, and the MP4 the API then serves is inspected with ffprobe.
/// </summary>
public sealed partial class RenderTests(AffiVideoApp app)
{
    // As long as a Hook may be, thirteen words and sixty characters, and written as if to break out of
    // a command line, a filter or a page. It is data: it is set in type, and that is all.
    private const string Hook = """Bạn "vẫn" $(dùng) <b>bình</b> 'nhựa' mỗi `ngày` ư? & % ; | \""";

    // Enough for the first render after the worker starts, which also loads the cut-out model.
    private static readonly TimeSpan LongestRender = TimeSpan.FromMinutes(8);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submitting_a_render_queues_a_job_for_the_worker_and_answers_at_once()
    {
        var rendered = await RenderedAsync(app);

        Assert.Equal(HttpStatusCode.Accepted, rendered.SubmittedStatus);
        Assert.Equal($"/api/v1/render-jobs/{rendered.Submitted.Id}", rendered.SubmittedLocation);
        Assert.Equal(RenderJobState.Queued, rendered.Submitted.State);
        Assert.Equal(rendered.Storyboard.Id, rendered.Submitted.StoryboardId);
        Assert.Null(rendered.Submitted.RenderedVideoId);
        Assert.Null(rendered.Submitted.Failure);
        Assert.Equal(0, rendered.Submitted.Attempt);
    }

    [Fact]
    public async Task A_job_moves_only_forward_through_its_stages_and_ends_completed_with_its_Rendered_Video()
    {
        var rendered = await RenderedAsync(app);

        // Polling does not catch every stage, but what it catches is in order, and each stage once.
        RenderJobState[] order =
        [
            RenderJobState.Queued, RenderJobState.Validating, RenderJobState.Planning, RenderJobState.GeneratingAssets,
            RenderJobState.Rendering, RenderJobState.QualityReview, RenderJobState.Completed,
        ];
        Assert.Equal(rendered.States.Distinct(), rendered.States);
        Assert.Equal(order.Where(rendered.States.Contains), rendered.States);
        Assert.Contains(RenderJobState.Rendering, rendered.States);
        Assert.Equal(RenderJobState.Completed, rendered.Job.State);
        Assert.Null(rendered.Job.Failure);
        Assert.Equal(1, rendered.Job.Attempt);
        Assert.Equal(rendered.Video.Id, rendered.Job.RenderedVideoId);
        Assert.True(rendered.Job.UpdatedAt > rendered.Job.CreatedAt);

        using var member = await app.SignedInAsync(rendered.Owner);
        var listed = await member.GetAsync<PagedResponse<RenderJobResponse>>(Renders(rendered.Variant, rendered.Storyboard.Version));
        Assert.Equal(rendered.Job, Assert.Single(listed.Items));
    }

    [Fact]
    public async Task The_Rendered_Video_is_ready_for_review_and_belongs_to_the_Storyboard_version_it_was_rendered_from()
    {
        var rendered = await RenderedAsync(app);

        Assert.Equal(RenderedVideoState.ReadyForReview, rendered.Video.State);
        Assert.Equal(rendered.Storyboard.Id, rendered.Video.StoryboardId);
        Assert.Equal(rendered.Job.Id, rendered.Video.RenderJobId);
        Assert.Equal(15_000, rendered.Video.DurationMs);
        Assert.Equal(rendered.Mp4.Length, rendered.Video.SizeInBytes);
    }

    [Fact]
    public async Task The_Rendered_Video_is_a_1080_by_1920_H264_and_AAC_MP4_tagged_BT709_and_as_long_as_its_Scenes()
    {
        var rendered = await RenderedAsync(app);

        var probe = await app.InWorkerAsync(
            ["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", "/tmp/probe/shared.mp4"],
            ("/tmp/probe/shared.mp4", rendered.Mp4));

        Assert.Equal(0, probe.ExitCode);
        using var described = JsonDocument.Parse(probe.Stdout);
        var streams = described.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal((1080, 1920), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32()));
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("yuv420p", video.GetProperty("pix_fmt").GetString());
        Assert.Equal("bt709", video.GetProperty("color_space").GetString());
        Assert.Equal("bt709", video.GetProperty("color_primaries").GetString());
        Assert.Equal("bt709", video.GetProperty("color_transfer").GetString());
        Assert.Equal("30/1", video.GetProperty("r_frame_rate").GetString());
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Contains("mp4", described.RootElement.GetProperty("format").GetProperty("format_name").GetString()!.Split(','));

        // The Scenes are joined end to end: every frame of every Scene and no other, 30 a second.
        var total = rendered.Storyboard.Scenes.Sum(scene => scene.DurationMs);
        Assert.Equal(15_000, total);
        Assert.Equal(total * 30 / 1000, int.Parse(video.GetProperty("nb_frames").GetString()!, CultureInfo.InvariantCulture));
        var seconds = double.Parse(described.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, total / 1000.0 - 0.05, total / 1000.0 + 0.05);
    }

    [Fact]
    public async Task With_no_audio_supplied_the_video_carries_a_silent_audio_track()
    {
        var rendered = await RenderedAsync(app);

        var listened = await app.InWorkerAsync(
            ["ffmpeg", "-hide_banner", "-i", "/tmp/probe/silent.mp4", "-vn", "-af", "volumedetect", "-f", "null", "-"],
            ("/tmp/probe/silent.mp4", rendered.Mp4));

        Assert.Equal(0, listened.ExitCode);
        var loudest = MaxVolume().Match(listened.Stderr);
        Assert.True(loudest.Success, listened.Stderr);
        var level = loudest.Groups[1].Value;
        Assert.True(level == "-inf" || double.Parse(level, CultureInfo.InvariantCulture) <= -90, loudest.Value);
    }

    [Fact]
    public async Task Every_Scene_draws_its_own_picture_and_the_Hook_is_on_screen_before_two_seconds()
    {
        var rendered = await RenderedAsync(app);
        // The last frame of the Hook's Scene, one frame late in each Scene after it, and the Hook's Scene at two seconds.
        var starts = rendered.Storyboard.Scenes.Select((_, index) => rendered.Storyboard.Scenes.Take(index).Sum(scene => scene.DurationMs)).ToList();
        var late = rendered.Storyboard.Scenes.Select((scene, index) => (starts[index] + scene.DurationMs * 0.8) / 1000).ToList();
        late[0] = (rendered.Storyboard.Scenes[0].DurationMs - 40) / 1000.0;
        Assert.Equal(13, Hook.Split(' ').Length);

        var frames = new List<SKBitmap>();
        try
        {
            foreach (var at in late.Prepend(2.0))
            {
                frames.Add(await FrameAsync(rendered.Mp4, at));
            }

            Assert.All(frames, frame => Assert.Equal((1080, 1920), (frame.Width, frame.Height)));
            // No two Scenes share a layout: far more than a few pixels differ between any two of them.
            for (var a = 1; a < frames.Count; a++)
            {
                for (var b = a + 1; b < frames.Count; b++)
                {
                    Assert.True(Different(frames[a], frames[b]) > 0.10, $"Scenes {a} and {b} look alike.");
                }
            }
            // At two seconds the Hook's type is where it is late in its Scene: every word has arrived.
            // Only the Product, which keeps moving between the two blocks of type, differs.
            Assert.True(Different(frames[0], frames[1], top: 130, bottom: 540) < 0.002, "The Hook's first block is still arriving at two seconds.");
            Assert.True(Different(frames[0], frames[1], top: 1160, bottom: 1540) < 0.002, "The Hook's second block is still arriving at two seconds.");
            // And there is type there to have arrived: the upper block is not empty backdrop.
            Assert.True(Dark(frames[0], top: 130, bottom: 540) > 0.02, "There is no type in the Hook's first block.");
        }
        finally
        {
            frames.ForEach(frame => frame.Dispose());
        }
    }

    [Fact]
    public async Task The_Product_is_cut_out_of_its_photos_and_the_cut_outs_are_kept_beside_them()
    {
        var rendered = await RenderedAsync(app);

        Assert.Empty(rendered.Video.UncutAssetIds);
        var stored = await app.StoredKeysAsync($"organizations/{rendered.OrganizationId}/products/{rendered.ProductId}/assets/");
        foreach (var photo in rendered.Photos)
        {
            Assert.Contains(stored, key => key.EndsWith($"/{photo.Id}.video-layer.png", StringComparison.Ordinal));
            Assert.Contains(stored, key => key.EndsWith($"/{photo.Id}.video-layer.json", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task The_Rendered_Video_is_stored_under_its_Organization_and_previewed_only_through_the_API()
    {
        var rendered = await RenderedAsync(app);
        using var member = await app.SignedInAsync(rendered.Owner);
        var path = $"/api/v1/rendered-videos/{rendered.Video.Id}";

        var preview = await member.GetAsync($"{path}/content");
        using var part = new HttpRequestMessage(HttpMethod.Get, $"{path}/content") { Headers = { Range = new RangeHeaderValue(0, 99) } };
        var firstBytes = await member.Http.SendAsync(part, Cancellation);
        using var anyone = new HttpClient();
        var key = $"organizations/{rendered.OrganizationId}/rendered-videos/{rendered.Video.Id}.mp4";
        var aroundTheApi = await anyone.GetAsync(app.StorageAddress(key), Cancellation);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("video/mp4", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal(rendered.Mp4, await preview.Content.ReadAsByteArrayAsync(Cancellation));
        // A player asks for the file in parts, to seek in it.
        Assert.Equal(HttpStatusCode.PartialContent, firstBytes.StatusCode);
        Assert.Equal(rendered.Mp4[..100], await firstBytes.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal([key], await app.StoredKeysAsync($"organizations/{rendered.OrganizationId}/rendered-videos/"));
        Assert.Equal(HttpStatusCode.Forbidden, aroundTheApi.StatusCode);
        Assert.Equal(rendered.Video, await member.GetAsync<RenderedVideoResponse>(path), SameVideo);
    }

    [Fact]
    public async Task Temporary_files_are_there_while_a_job_renders_and_gone_once_it_has_ended()
    {
        var rendered = await RenderedAsync(app);

        Assert.True(rendered.WorkFolderSeenWhileRendering, "The job's folder was never seen while it rendered.");
        var after = await app.InWorkerAsync(["sh", "-c", "ls -A /tmp/affivideo-render"]);
        Assert.DoesNotContain(rendered.Job.Id.ToString("N"), after.Stdout);
    }

    [Fact]
    public async Task A_Project_that_has_a_Rendered_Video_cannot_be_deleted()
    {
        var rendered = await RenderedAsync(app);
        using var member = await app.SignedInAsync(rendered.Owner);

        var response = await member.DeleteAsync($"{ProjectTests.Projects}/{rendered.Variant.ProjectId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Rendered Video", (await ReadAsync<ProblemDetails>(response)).Detail);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/rendered-videos/{rendered.Video.Id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"{StoryboardTests.Storyboards(rendered.Variant)}/1")).StatusCode);
    }

    [Fact]
    public async Task A_photo_the_Product_cannot_be_cut_out_of_is_shown_whole_on_a_card_and_its_files_go_with_the_photo()
    {
        await app.WorkerAsync();
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await StoryboardTests.NewProductAsync(member);
        // One flat colour from edge to edge: there is no Product in it to find.
        var photo = await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15);
        var storyboard = await StoryboardTests.GeneratedAsync(member, variant);
        var layers = $"organizations/{organization.Id}/products/{product}/assets/{photo.Id}.video-layer";

        var job = await EndedAsync(member, await SubmittedAsync(member, variant, storyboard.Version));

        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await app.WorkerLogAsync()}");
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
        Assert.Equal([photo.Id], video.UncutAssetIds);
        Assert.Equal(2, (await app.StoredKeysAsync(layers)).Length);

        // What a render made from a photo is removed with the photo.
        var removed = await member.DeleteAsync($"{ProductAssetTests.Assets(product)}/{photo.Id}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty(await app.StoredKeysAsync(layers));
    }

    [Fact]
    public async Task A_render_fails_with_a_reason_when_a_photo_its_Storyboard_shows_has_been_removed()
    {
        await app.WorkerAsync();
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await StoryboardTests.NewProductAsync(member);
        var photo = await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var storyboard = await StoryboardTests.GeneratedAsync(member, variant);
        (await member.DeleteAsync($"{ProductAssetTests.Assets(product)}/{photo.Id}")).EnsureSuccessStatusCode();

        var first = await EndedAsync(member, await SubmittedAsync(member, variant, storyboard.Version));
        var second = await EndedAsync(member, await SubmittedAsync(member, variant, storyboard.Version));

        Assert.Equal(RenderJobState.Failed, first.State);
        Assert.Null(first.RenderedVideoId);
        Assert.Equal(
            "Scene 1 shows a photo that has since been removed from the Product. " +
            "Scene 2 shows a photo that has since been removed from the Product. " +
            "Scene 3 shows a photo that has since been removed from the Product. " +
            "Scene 4 shows a photo that has since been removed from the Product. " +
            "Generate the Storyboard again, then render that version.",
            first.Failure?.Message);
        // The stage, and a category that says trying again would not have helped: it was tried once.
        Assert.Equal(RenderJobState.Validating, first.Failure!.Stage);
        Assert.Equal(RenderFailureCategory.InvalidInput, first.Failure.Category);
        Assert.Null(first.Failure.Detail);
        Assert.Equal(1, first.Attempt);
        Assert.Empty(await app.StoredKeysAsync($"organizations/{organization.Id}/rendered-videos/"));
        // Rendering again is another job. The newest is listed first.
        var listed = await member.GetAsync<PagedResponse<RenderJobResponse>>(Renders(variant, storyboard.Version));
        Assert.Equal([second.Id, first.Id], listed.Items.Select(job => job.Id));
        Assert.Equal(2, listed.Total);
        Assert.Equal([first.Id], (await member.GetAsync<PagedResponse<RenderJobResponse>>(
            $"{Renders(variant, storyboard.Version)}?page=2&pageSize=1")).Items.Select(job => job.Id));

        // A job that made nothing does not hold its Project.
        var deleted = await member.DeleteAsync($"{ProjectTests.Projects}/{variant.ProjectId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/render-jobs/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task Rendering_an_edited_version_draws_only_the_Scene_that_changed_and_reuses_the_clips_of_the_others()
    {
        await app.WorkerAsync();
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await StoryboardTests.NewProductAsync(member, "Bình giữ nhiệt Lumo 500");
        // It came already cut out, so no model runs.
        await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, Bottle(onBackdrop: false));
        await StoryboardTests.ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        var clips = $"organizations/{organization.Id}/scene-clips/";

        var original = await VideoAsync(member, variant, first.Version);
        var keptAfterFirst = await app.StoredKeysAsync(clips);
        // Only the reveal says something else. Its neighbours are drawn from what they are handed, and that has not changed.
        var second = await StoryboardEditTests.EditedAsync(
            member, variant, first.Version, StoryboardEditTests.Changing(first, new SceneEditRequest(2, OnScreenText: ["Lumo 500 mới về"])));
        var edited = await VideoAsync(member, variant, second.Version);
        var again = await VideoAsync(member, variant, first.Version);

        Assert.Equal([1, 2, 3, 4], original.Video.DrawnScenePositions);
        Assert.Equal([2], edited.Video.DrawnScenePositions);
        Assert.Empty(again.Video.DrawnScenePositions);
        // Each Rendered Video says which Storyboard version it came from.
        Assert.Equal((1, 2, 1), (original.Video.StoryboardVersion, edited.Video.StoryboardVersion, again.Video.StoryboardVersion));
        Assert.Equal((first.Id, second.Id, first.Id), (original.Video.StoryboardId, edited.Video.StoryboardId, again.Video.StoryboardId));
        // One clip for each Scene that was ever drawn, under the Organization.
        Assert.Equal(4, keptAfterFirst.Length);
        Assert.Equal(5, (await app.StoredKeysAsync(clips)).Length);

        // What was reused is frame for frame what was drawn the first time, and the Scene that changed is not.
        var starts = first.Scenes.Select((_, index) => first.Scenes.Take(index).Sum(scene => scene.DurationMs)).ToList();
        for (var index = 0; index < first.Scenes.Count; index++)
        {
            var late = (starts[index] + first.Scenes[index].DurationMs * 0.8) / 1000;
            using var before = await FrameAsync(original.Mp4, late);
            using var after = await FrameAsync(edited.Mp4, late);
            using var rendered = await FrameAsync(again.Mp4, late);
            Assert.Equal(0, Different(before, rendered));
            if (index == 1) Assert.True(Different(before, after) > 0.005, "The Scene that was edited looks as it did.");
            else Assert.Equal(0, Different(before, after));
        }
    }

    // Renders the version and waits for its Rendered Video and the MP4 the API serves for it.
    private async Task<(RenderedVideoResponse Video, byte[] Mp4)> VideoAsync(Browser member, VariantResponse variant, int version)
    {
        var job = await EndedAsync(member, await SubmittedAsync(member, variant, version));
        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await app.WorkerLogAsync()}");
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
        var content = await member.GetAsync($"/api/v1/rendered-videos/{video.Id}/content");
        content.EnsureSuccessStatusCode();
        return (video, await content.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Renders_are_only_found_under_the_Storyboard_version_they_belong_to()
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var variant = await StoryboardTests.ReadyVariantAsync(member);
        var other = await StoryboardTests.ReadyVariantAsync(member);
        await StoryboardTests.GeneratedAsync(member, variant);
        var underOtherProject = $"{VariantTests.Variants(other.ProjectId)}/{variant.Id}/storyboards/1/renders";

        HttpResponseMessage[] responses =
        [
            await SubmitAsync(member, Renders(variant, 2)),
            await member.GetAsync(Renders(variant, 2)),
            await SubmitAsync(member, Renders(other, 1)),
            await member.GetAsync(Renders(other, 1)),
            await SubmitAsync(member, underOtherProject),
            await member.GetAsync(underOtherProject),
            await member.GetAsync($"/api/v1/render-jobs/{Guid.NewGuid()}"),
            await member.GetAsync($"/api/v1/rendered-videos/{Guid.NewGuid()}"),
            await member.GetAsync($"/api/v1/rendered-videos/{Guid.NewGuid()}/content"),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(0, (await member.GetAsync<PagedResponse<RenderJobResponse>>(Renders(variant, 1))).Total);
    }

    [Fact]
    public async Task Renders_are_refused_to_someone_who_is_not_signed_in()
    {
        var rendered = await RenderedAsync(app);
        using var stranger = app.NewBrowser();

        HttpResponseMessage[] responses =
        [
            await SubmitAsync(stranger, Renders(rendered.Variant, 1)),
            await stranger.GetAsync(Renders(rendered.Variant, 1)),
            await stranger.GetAsync($"/api/v1/render-jobs/{rendered.Job.Id}"),
            await stranger.GetAsync($"/api/v1/rendered-videos/{rendered.Video.Id}"),
            await stranger.GetAsync($"/api/v1/rendered-videos/{rendered.Video.Id}/content"),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    internal static string Renders(VariantResponse variant, int version) =>
        $"{StoryboardTests.Storyboards(variant)}/{version}/renders";

    /// <summary>The header a render is submitted with, so that a request sent twice queues one job.</summary>
    internal const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Submits a render as one click does: with a key of its own unless it is given one.</summary>
    internal static Task<HttpResponseMessage> SubmitAsync(Browser member, string renders, string? idempotencyKey = null) =>
        member.PostAsync(renders, new { }, (IdempotencyKeyHeader, idempotencyKey ?? Guid.NewGuid().ToString()));

    internal static async Task<RenderJobResponse> SubmittedAsync(Browser member, VariantResponse variant, int version)
    {
        var response = await SubmitAsync(member, Renders(variant, version));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await ReadAsync<RenderJobResponse>(response);
    }

    /// <summary>Asks for the job, as the web app does, until it has ended.</summary>
    /// <param name="each">Called with every answer.</param>
    internal static async Task<RenderJobResponse> EndedAsync(
        Browser member, RenderJobResponse job, Func<RenderJobResponse, Task>? each = null)
    {
        var until = DateTimeOffset.UtcNow + LongestRender;
        while (true)
        {
            job = await member.GetAsync<RenderJobResponse>($"/api/v1/render-jobs/{job.Id}");
            if (each is not null) await each(job);
            if (job.State.HasEnded()) return job;
            Assert.True(DateTimeOffset.UtcNow < until, $"The job was still {job.State} after {LongestRender.TotalMinutes} minutes.");
            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancellation);
        }
    }

    /// <summary>
    /// One Storyboard version rendered by the worker, for every test that only reads
    /// the result: a 15-second video of a Product with two photos, one that came
    /// already cut out and one taken on a plain backdrop, which the model cuts out.
    /// </summary>
    internal static Task<Rendered> RenderedAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        await app.WorkerAsync();
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await StoryboardTests.NewProductAsync(member, "Bình giữ nhiệt Lumo 500");
        var cutOut = await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, Bottle(onBackdrop: false));
        var studio = await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, Bottle(onBackdrop: true));
        await StoryboardTests.ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        await StoryboardTests.ConfirmedFactAsync(member, product, """Thép không gỉ; `ls` && "x" <i>x</i> $HOME""");
        var variant = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15, hook: Hook);
        var storyboard = await StoryboardTests.GeneratedAsync(member, variant);

        var response = await SubmitAsync(member, Renders(variant, storyboard.Version));
        var submitted = await ReadAsync<RenderJobResponse>(response);
        var states = new List<RenderJobState> { submitted.State };
        var folderSeen = false;
        var job = await EndedAsync(member, submitted, async polled =>
        {
            if (states[^1] != polled.State) states.Add(polled.State);
            if (polled.State == RenderJobState.Rendering && !folderSeen)
            {
                folderSeen = (await app.InWorkerAsync(["test", "-d", $"/tmp/affivideo-render/{submitted.Id:N}-1/bundle"])).ExitCode == 0;
            }
        });
        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await app.WorkerLogAsync()}");

        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
        var content = await member.GetAsync($"/api/v1/rendered-videos/{video.Id}/content");
        content.EnsureSuccessStatusCode();
        var mp4 = await content.Content.ReadAsByteArrayAsync(Cancellation);
        // To watch what the tests rendered: AFFIVIDEO_TEST_VIDEO=some/file.mp4 dotnet test
        if (Environment.GetEnvironmentVariable("AFFIVIDEO_TEST_VIDEO") is { Length: > 0 } keep)
        {
            await File.WriteAllBytesAsync(keep, mp4, Cancellation);
        }
        return new Rendered(
            organization.Owner, organization.Id, product, [cutOut, studio], variant, storyboard,
            response.StatusCode, response.Headers.Location?.OriginalString, submitted, states, job, video, mp4, folderSeen);
    });

    internal sealed record Rendered(
        Credentials Owner,
        Guid OrganizationId,
        Guid ProductId,
        IReadOnlyList<ProductAssetResponse> Photos,
        VariantResponse Variant,
        StoryboardResponse Storyboard,
        HttpStatusCode SubmittedStatus,
        string? SubmittedLocation,
        RenderJobResponse Submitted,
        IReadOnlyList<RenderJobState> States,
        RenderJobResponse Job,
        RenderedVideoResponse Video,
        byte[] Mp4,
        bool WorkFolderSeenWhileRendering);

    /// <summary>
    /// A photo of a drinks bottle, drawn: on nothing, as a catalogue photo that came
    /// already cut out, or on a plain pale backdrop, as one taken in a studio.
    /// </summary>
    internal static byte[] Bottle(bool onBackdrop)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(900, 1200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(onBackdrop ? new SKColor(236, 233, 228) : SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = new SKColor(24, 92, 140);
            canvas.DrawRoundRect(new SKRect(300, 330, 600, 1080), 70, 70, paint);
            paint.Color = new SKColor(40, 44, 52);
            canvas.DrawRoundRect(new SKRect(360, 150, 540, 350), 30, 30, paint);
            paint.Color = new SKColor(225, 232, 238);
            canvas.DrawRoundRect(new SKRect(340, 560, 560, 800), 24, 24, paint);
            paint.Color = new SKColor(16, 60, 96);
            canvas.DrawRect(new SKRect(300, 900, 600, 930), paint);
        }
        using var encoded = bitmap.Encode(onBackdrop ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 95);
        return encoded.ToArray();
    }

    // One frame of the video as a picture, taken out by FFmpeg in the worker's container.
    private async Task<SKBitmap> FrameAsync(byte[] mp4, double seconds)
    {
        // The picture goes where the worker's own user may write; the video was put there for it.
        var name = $"frame-{Guid.NewGuid():N}";
        var taken = await app.InWorkerAsync(
            [
                "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", $"/tmp/probe/{name}.mp4", "-frames:v", "1", $"/tmp/{name}.png",
            ],
            ($"/tmp/probe/{name}.mp4", mp4));
        Assert.True(taken.ExitCode == 0, taken.Stderr);
        var png = await (await app.WorkerAsync()).ReadFileAsync($"/tmp/{name}.png", Cancellation);
        return SKBitmap.Decode(png);
    }

    // The share of pixels, within these rows, that are plainly not the same in the two frames.
    private static double Different(SKBitmap a, SKBitmap b, int top = 0, int bottom = 1920)
    {
        var different = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                SKColor one = a.GetPixel(x, y), other = b.GetPixel(x, y);
                if (Math.Abs(one.Red - other.Red) + Math.Abs(one.Green - other.Green) + Math.Abs(one.Blue - other.Blue) > 60) different++;
            }
        }
        return (double)different / ((bottom - top) * a.Width);
    }

    // The share of pixels, within these rows, dark enough to be type on the pale backdrop.
    private static double Dark(SKBitmap frame, int top, int bottom)
    {
        var dark = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame.GetPixel(x, y);
                if (pixel.Red + pixel.Green + pixel.Blue < 240) dark++;
            }
        }
        return (double)dark / ((bottom - top) * frame.Width);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;

    // When each was created is left out: the database keeps a time less finely than .NET does.
    private static readonly IEqualityComparer<RenderedVideoResponse> SameVideo =
        EqualityComparer<RenderedVideoResponse>.Create((a, b) =>
            JsonSerializer.Serialize(a! with { CreatedAt = default }, AffiVideoApp.Json) ==
            JsonSerializer.Serialize(b! with { CreatedAt = default }, AffiVideoApp.Json));

    [GeneratedRegex(@"max_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MaxVolume();
}
