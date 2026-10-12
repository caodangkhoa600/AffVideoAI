using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using SkiaSharp;
using static AffiVideo.Api.Tests.StillGenerationTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// A render with a generated backdrop, as a member sees it: the approved Still is
/// behind the Product's own Cut-out in the Hook's Scene, and the MP4 the API serves
/// is inspected frame by frame. The Still is the fake image provider's.
/// </summary>
public sealed class HybridRenderTests(AffiVideoApp app)
{
    private const decimal Rate = 0.04m;

    // Rows under everything a Product Showcase Hook sets in type or stands the Product on: backdrop and nothing else.
    private static readonly SKRectI Below = new(0, 1600, 1080, 1900);

    // Inside the bottle's blue body, where the Hook rests the Product.
    private static readonly SKRectI Body = new(700, 1020, 740, 1060);

    // Around where the Hook rests the Product, and well inside a photo shown whole on its Card.
    private static readonly SKRectI OnCard = new(620, 770, 820, 970);

    private static readonly SKRectI Whole = new(0, 0, 1080, 1920);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Hooks_Scene_shows_the_Still_behind_the_Products_own_Cut_out_and_every_other_Scene_is_as_in_Product_Lock()
    {
        var rendered = await RenderedAsync(app);
        var scenes = rendered.Storyboard.Scenes;
        var inHook = scenes[0].DurationMs * 0.6 / 1000;
        var inReveal = (scenes[0].DurationMs + scenes[1].DurationMs * 0.8) / 1000;

        using var locked = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, inHook);
        using var hybrid = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, inHook);
        using var lockedReveal = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, inReveal);
        using var hybridReveal = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, inReveal);

        Assert.Equal(Technique.GeneratedBackdrop, scenes[0].Technique);
        Assert.Equal(rendered.Still.Id, scenes[0].StillId);
        // Where Product Lock has the studio backdrop, there is another picture.
        Assert.True(Changed(locked, hybrid, Below) > 0.5, "The Hook's Scene has the backdrop it has in Product Lock.");
        // The Product is its photo, pixel for pixel what Product Lock shows, and nothing of the Still comes through it.
        Assert.True(Same(locked, hybrid, Body));
        var body = hybrid.GetPixel(720, 1040);
        Assert.True(body.Blue > body.Red + 60, $"The bottle's body is {body} where it should be its own blue.");
        // No other Scene is generated, so no other Scene differs.
        Assert.True(Same(lockedReveal, hybridReveal, Whole));
    }

    [Fact]
    public async Task The_backdrop_moves_slowly_from_the_start_of_its_Scene_to_the_end()
    {
        var rendered = await RenderedAsync(app);
        var duration = rendered.Storyboard.Scenes[0].DurationMs / 1000.0;
        // Once the Hook has arrived, halfway, and just before the Still gives way to the template's own ground.
        double early = 1.0, halfway = (1.0 + duration - 0.4) / 2, late = duration - 0.4;

        using var first = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, early);
        using var next = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, early + 1 / 30.0);
        using var middle = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, halfway);
        using var last = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, late);
        using var end = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, duration - 1 / 30.0);
        using var lockedFirst = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, early);
        using var lockedLast = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, late);
        using var lockedEnd = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, duration - 1 / 30.0);

        // It is still moving in the second half of the Scene as in the first: the movement is not an arrival.
        Assert.True(Changed(first, middle, Below) > 0.02, "The backdrop does not move in the first half of its Scene.");
        Assert.True(Changed(middle, last, Below) > 0.02, "The backdrop does not move in the second half of its Scene.");
        // From one frame to the next it barely moves at all.
        Assert.True(Changed(first, next, Below) < 0.01, "The backdrop jumps from one frame to the next.");
        // The Scene after is drawn knowing nothing of the Still, so this one ends where Product Lock's does: on the studio backdrop.
        Assert.True(Changed(end, lockedEnd, Below) < 0.02, "The Still is still there on the last frame of its Scene.");
        // The studio backdrop of Product Lock, in the same rows, does not move.
        Assert.True(Same(lockedFirst, lockedLast, Below));
    }

    [Fact]
    public async Task The_video_is_a_1080_by_1920_H264_and_AAC_MP4_as_long_as_its_Scenes()
    {
        var rendered = await RenderedAsync(app);
        await rendered.Worker.CopyAsync(rendered.Hybrid.Mp4, "/tmp/probe/hybrid.mp4", ct: Cancellation);

        var probe = await rendered.Worker.ExecAsync(
            ["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", "/tmp/probe/hybrid.mp4"], Cancellation);

        Assert.Equal(0, probe.ExitCode);
        using var described = JsonDocument.Parse(probe.Stdout);
        var streams = described.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal((1080, 1920), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32()));
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal(15 * 30, int.Parse(video.GetProperty("nb_frames").GetString()!, CultureInfo.InvariantCulture));
        var seconds = double.Parse(described.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 14.95, 15.05);
        Assert.Equal(15_000, rendered.Hybrid.Rendered.DurationMs);
        Assert.Equal(RenderedVideoState.ReadyForReview, rendered.Hybrid.Rendered.State);
        Assert.Empty(rendered.Hybrid.Rendered.UncutAssetIds);
    }

    [Fact]
    public async Task The_render_generates_nothing_and_is_not_a_cost_of_generation()
    {
        var rendered = await RenderedAsync(app);

        // The worker that rendered has no image provider, and the Project has what it had before: one Still, paid for once.
        Assert.Equal(RenderJobState.Completed, rendered.Hybrid.Job.State);
        Assert.Equal([rendered.Still.Id], rendered.StillsAfterRendering.Items.Select(still => still.Id));
        Assert.Equal(Rate, rendered.GenerationCostAfterRendering);
        Assert.Equal(Rate, Assert.Single(rendered.StillsAfterRendering.Items).Cost?.EstimatedAmount);
        // The Still is as it was approved: the render changed nothing about it.
        Assert.Equal(StillState.Approved, rendered.StillsAfterRendering.Items[0].State);
        // To the precision the database keeps a time at.
        Assert.Equal(rendered.Still.UpdatedAt, rendered.StillsAfterRendering.Items[0].UpdatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Only_the_Scene_with_the_Still_is_drawn_for_it_and_only_that_Scene_is_drawn_again_when_another_Still_is_approved()
    {
        var rendered = await RenderedAsync(app);
        var hookEnds = rendered.Storyboard.Scenes[0].DurationMs / 1000.0;

        // Product Lock drew every Scene. The Hybrid Variant has the same Scenes but for the one with the Still.
        Assert.Equal([1, 2, 3, 4], rendered.Locked.Rendered.DrawnScenePositions);
        Assert.Equal([1], rendered.Hybrid.Rendered.DrawnScenePositions);
        // Another Still is another picture of the same Scene: the version that refers to it draws that Scene and no other.
        Assert.NotEqual(rendered.Still.Id, rendered.AnotherStill.Id);
        Assert.Equal(rendered.AnotherStill.Id, rendered.AnotherStoryboard.Scenes[0].StillId);
        Assert.Equal(rendered.Storyboard.Version + 1, rendered.AnotherStoryboard.Version);
        Assert.Equal([1], rendered.WithAnotherStill.Rendered.DrawnScenePositions);
        // The version made with the first Still is as it was: it renders with that Still, and nothing has to be drawn for it.
        Assert.Equal(StillState.Rejected, rendered.StillAfterReplacement.State);
        Assert.Empty(rendered.AfterReplacement.Rendered.DrawnScenePositions);
        Assert.Equal(rendered.Hybrid.Rendered.StoryboardId, rendered.AfterReplacement.Rendered.StoryboardId);
        using var before = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, hookEnds + 1);
        using var after = await FrameAsync(rendered.Worker, rendered.WithAnotherStill.Mp4, hookEnds + 1);
        Assert.True(Same(before, after, Whole));
    }

    [Fact]
    public async Task A_version_whose_generated_backdrop_has_no_approved_Still_fails_validation_with_the_reason()
    {
        var rendered = await RenderedAsync(app);

        // The version from before the Still was approved refers to none.
        var job = rendered.BeforeApproval;
        Assert.Equal(RenderJobState.Failed, job.State);
        Assert.Null(job.RenderedVideoId);
        Assert.Equal(
            "Scene 1 has a generated backdrop and no approved Still. " +
            "Approve a Still for it at Storyboard review, then render the version that makes.",
            job.Failure?.Message);
        Assert.Equal((RenderJobState.Validating, RenderFailureCategory.InvalidInput), (job.Failure!.Stage, job.Failure.Category));
        // Trying again would not have helped: it was tried once.
        Assert.Equal(1, job.Attempt);
    }

    [Fact]
    public async Task A_photo_with_no_usable_Cut_out_is_shown_whole_on_its_Card_over_the_Still()
    {
        var rendered = await RenderedAsync(app);
        var at = rendered.Storyboard.Scenes[0].DurationMs * 0.6 / 1000;

        using var card = await FrameAsync(rendered.Worker, rendered.OnCard.Mp4, at);
        using var cutOut = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, at);
        using var locked = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, at);

        Assert.Equal([rendered.CardPhoto], rendered.OnCard.Rendered.UncutAssetIds);
        // The same Still, moved the same way, as behind a Product that was cut out.
        Assert.True(Same(card, cutOut, Below));
        Assert.True(Changed(card, locked, Below) > 0.2, "The Card is on the studio backdrop.");
        // The photo is one flat colour from edge to edge, and nothing of the Still comes through it.
        var centre = card.GetPixel(720, 870);
        using var flat = new SKBitmap(card.Info);
        flat.Erase(centre);
        Assert.True(Same(card, flat, OnCard));
    }

    [Fact]
    public async Task Luxury_Cinematic_draws_the_Still_behind_its_Hook_too_and_ends_the_Scene_on_its_own_ground()
    {
        var rendered = await RenderedAsync(app);
        var duration = rendered.LuxuryStoryboard.Scenes[0].DurationMs / 1000.0;

        using var locked = await FrameAsync(rendered.Worker, rendered.LuxuryLocked.Mp4, duration * 0.6);
        using var hybrid = await FrameAsync(rendered.Worker, rendered.Luxury.Mp4, duration * 0.6);
        using var lockedEnd = await FrameAsync(rendered.Worker, rendered.LuxuryLocked.Mp4, duration - 1 / 30.0);
        using var hybridEnd = await FrameAsync(rendered.Worker, rendered.Luxury.Mp4, duration - 1 / 30.0);

        Assert.Equal(CreativeTemplate.LuxuryCinematic, rendered.LuxuryStoryboard.CreativeTemplate);
        Assert.Equal(Technique.GeneratedBackdrop, rendered.LuxuryStoryboard.Scenes[0].Technique);
        Assert.NotNull(rendered.LuxuryStoryboard.Scenes[0].StillId);
        // Under the Hook's type there is the dark in Product Lock, and a picture here.
        Assert.True(Changed(locked, hybrid, Below) > 0.5, "Luxury Cinematic's Hook has the ground it has in Product Lock.");
        Assert.True(Changed(lockedEnd, hybridEnd, Below) < 0.02, "The Still is still there on the last frame of its Scene.");
        Assert.Equal([1], rendered.Luxury.Rendered.DrawnScenePositions);
    }

    /// <summary>
    /// One Product, with a photo that came already cut out, in one Project of two Variants with the
    /// same Hook: one in Product Lock, and one in Hybrid whose Hook's Scene has a generated backdrop.
    /// The Still was made by a worker that has the fake image provider, and approved. Both were then
    /// rendered to 15-second videos by a worker that has no image provider at all. So was a Hybrid
    /// Variant of a second Product, whose only photo has no Product in it to cut out. Last, another
    /// Still was asked for in place of the first, approved, and the version that makes rendered. The
    /// second Project also has two Luxury Cinematic Variants, one in each Render Mode, both rendered.
    /// </summary>
    internal static Task<HybridRendered> RenderedAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        var stack = await app.NewStackAsync(FakeImages(Rate));
        var member = await stack.NewMemberAsync();
        var product = await NewProductAsync(member, "Bình giữ nhiệt Lumo 500");
        await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, RenderTests.Bottle(onBackdrop: false));
        await ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { TargetDurationSeconds = 15 });
        var locked = await VariantTests.AddAsync(member, project.Id);
        var hybrid = await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        await GeneratedAsync(member, locked);
        await GeneratedAsync(member, hybrid);

        // One flat colour from edge to edge: there is no Product in it to find.
        var plain = await NewProductAsync(member, "Lumo 500");
        var cardPhoto = await UploadPhotoAsync(member, plain);
        await ConfirmedFactAsync(member, plain, "Giữ lạnh suốt 24 giờ");
        var plainProject = await ProjectTests.CreateAsync(member, ProjectTests.Valid(plain) with { TargetDurationSeconds = 15 });
        var onCard = await VariantTests.AddAsync(member, plainProject.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        await GeneratedAsync(member, onCard);
        var luxuryLocked = await VariantTests.AddAsync(member, plainProject.Id, new VariantRequest(CreativeTemplate.LuxuryCinematic, VariantTests.Valid().Hook));
        var luxury = await VariantTests.AddAsync(
            member, plainProject.Id, new VariantRequest(CreativeTemplate.LuxuryCinematic, VariantTests.Valid().Hook, RenderMode.Hybrid));
        await GeneratedAsync(member, luxuryLocked);
        await GeneratedAsync(member, luxury);

        var images = await stack.StartWorkerAsync(FakeImageWorker);
        var still = await ApprovedAsync(member, await EndedAsync(member, await ConfirmedAsync(member, hybrid, 1), images));
        await ApprovedAsync(member, await EndedAsync(member, await ConfirmedAsync(member, onCard, 1), images));
        await ApprovedAsync(member, await EndedAsync(member, await ConfirmedAsync(member, luxury, 1), images));
        var luxuryStoryboard = (await ListAsync(member, luxury)).Items[0];
        var storyboard = (await ListAsync(member, hybrid)).Items[0];
        await RenderStack.KillAsync(images);

        // Whatever this worker draws, it generated none of it: it has nothing to generate with.
        var worker = await stack.StartWorkerAsync();
        var lockedVideo = await VideoAsync(member, worker, locked, 1);
        var hybridVideo = await VideoAsync(member, worker, hybrid, storyboard.Version);
        var cardVideo = await VideoAsync(member, worker, onCard, 2);
        var luxuryLockedVideo = await VideoAsync(member, worker, luxuryLocked, 1);
        var luxuryVideo = await VideoAsync(member, worker, luxury, luxuryStoryboard.Version);
        var beforeApproval = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, hybrid, 1));
        var stills = await StillsAsync(member, hybrid);
        var spent = (await member.GetAsync<GenerationEstimateResponse>(Generation(hybrid, storyboard.Version))).Spent;

        // Another Still in place of the first. A worker with no image provider would take
        // the Still and fail it, so it makes way for one that has.
        await RenderStack.KillAsync(worker);
        var withImages = await stack.StartWorkerAsync(FakeImageWorker);
        var asked = await member.PostAsync($"/api/v1/stills/{still.Id}/another", new AnotherStillRequest(Rate), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        var replaced = await member.GetAsync<StillResponse>($"/api/v1/stills/{still.Id}");
        var afterReplacement = await VideoAsync(member, withImages, hybrid, storyboard.Version);
        var another = await ApprovedAsync(
            member, await EndedAsync(member, (await asked.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!, withImages));
        var anotherStoryboard = (await ListAsync(member, hybrid)).Items[0];
        var anotherVideo = await VideoAsync(member, withImages, hybrid, anotherStoryboard.Version);

        return new HybridRendered(
            member, withImages, hybrid, storyboard, still, lockedVideo, hybridVideo, cardVideo, cardPhoto.Id, beforeApproval, stills, spent,
            another, anotherStoryboard, anotherVideo, replaced, afterReplacement, luxuryStoryboard, luxuryLockedVideo, luxuryVideo);
    });

    private static async Task<StillResponse> ApprovedAsync(Browser member, StillResponse still)
    {
        Assert.True(still.State == StillState.ReadyForReview, $"The Still ended {still.State}: {still.Failure?.Message}");
        var approval = await member.PostAsync($"/api/v1/stills/{still.Id}/approve", new { });
        approval.EnsureSuccessStatusCode();
        return (await approval.Content.ReadFromJsonAsync<StillResponse>(AffiVideoApp.Json, Cancellation))!;
    }

    /// <param name="Member">Signed in as the Owner of the Organization.</param>
    /// <param name="Worker">A worker that is still running, where FFmpeg is.</param>
    /// <param name="Storyboard">The Hybrid Variant's version that refers to the Still.</param>
    /// <param name="OnCard">The video of the Product whose photo could not be cut out.</param>
    /// <param name="BeforeApproval">The job that was to render the Hybrid Variant's first version, which refers to no Still.</param>
    /// <param name="StillsAfterRendering">The Project's Stills once the three videos were rendered, and before another was asked for.</param>
    /// <param name="AnotherStoryboard">The version that refers to the Still asked for in place of the first.</param>
    /// <param name="StillAfterReplacement">The first Still once another had been asked for in its place.</param>
    /// <param name="AfterReplacement"><paramref name="Storyboard"/> rendered again once its Still had been replaced.</param>
    internal sealed record HybridRendered(
        Browser Member, IContainer Worker, VariantResponse Variant, StoryboardResponse Storyboard, StillResponse Still,
        Video Locked, Video Hybrid, Video OnCard, Guid CardPhoto, RenderJobResponse BeforeApproval,
        PagedResponse<StillResponse> StillsAfterRendering, decimal GenerationCostAfterRendering,
        StillResponse AnotherStill, StoryboardResponse AnotherStoryboard, Video WithAnotherStill,
        StillResponse StillAfterReplacement, Video AfterReplacement,
        StoryboardResponse LuxuryStoryboard, Video LuxuryLocked, Video Luxury);

    internal sealed record Video(RenderJobResponse Job, RenderedVideoResponse Rendered, byte[] Mp4);

    // Renders the version and waits for its Rendered Video and the MP4 the API serves for it.
    private static async Task<Video> VideoAsync(Browser member, IContainer worker, VariantResponse variant, int version)
    {
        var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, variant, version));
        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await RenderStack.LogAsync(worker)}");
        var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
        var content = await member.GetAsync($"/api/v1/rendered-videos/{video.Id}/content");
        content.EnsureSuccessStatusCode();
        var mp4 = await content.Content.ReadAsByteArrayAsync(Cancellation);
        // To watch what the tests rendered: AFFIVIDEO_TEST_VIDEO=some/file.mp4 dotnet test
        if (Environment.GetEnvironmentVariable("AFFIVIDEO_TEST_VIDEO") is { Length: > 0 } keep)
        {
            await File.WriteAllBytesAsync(Path.ChangeExtension(keep, $".{video.Id:N}.{variant.RenderMode}.mp4"), mp4, Cancellation);
        }
        return new Video(job, video, mp4);
    }

    // One frame of the video as a picture, taken out by FFmpeg in the worker's container.
    private static async Task<SKBitmap> FrameAsync(IContainer worker, byte[] mp4, double seconds)
    {
        var name = $"frame-{Guid.NewGuid():N}";
        await worker.CopyAsync(mp4, $"/tmp/probe/{name}.mp4", ct: Cancellation);
        var taken = await worker.ExecAsync(
            [
                "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", seconds.ToString("0.###", CultureInfo.InvariantCulture),
                "-i", $"/tmp/probe/{name}.mp4", "-frames:v", "1", $"/tmp/{name}.png",
            ],
            Cancellation);
        Assert.True(taken.ExitCode == 0, taken.Stderr);
        return SKBitmap.Decode(await worker.ReadFileAsync($"/tmp/{name}.png", Cancellation));
    }

    // Whether the two frames show the same picture within the box. A stray pixel in a thousand is the encoder's doing.
    private static bool Same(SKBitmap a, SKBitmap b, SKRectI box) => Changed(a, b, box) < 0.001;

    // The share of pixels, within the box, that are not the same in the two frames beyond what encoding does to them.
    private static double Changed(SKBitmap a, SKBitmap b, SKRectI box)
    {
        var changed = 0;
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                SKColor one = a.GetPixel(x, y), other = b.GetPixel(x, y);
                if (Math.Abs(one.Red - other.Red) + Math.Abs(one.Green - other.Green) + Math.Abs(one.Blue - other.Blue) > 30) changed++;
            }
        }
        return (double)changed / (box.Width * box.Height);
    }
}
