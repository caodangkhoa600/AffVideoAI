using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using SkiaSharp;
using static AffiVideo.Api.Tests.GeneratedFootageTests;
using static AffiVideo.Api.Tests.HybridRenderTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// A render with generated footage, as a member sees it: the approved Clip fills its
/// Scene under the Scene's type, with no Product in it, and the MP4 the API serves is
/// inspected frame by frame. The Stills and Clips are the fake providers'.
/// </summary>
public sealed class GeneratedFootageRenderTests(AffiVideoApp app)
{
    private const decimal StillRate = 0.04m;
    private const decimal ClipRate = 0.10m;

    // Where Product Showcase stands the Product in its Facts Scene: the bottle's label and the body around it.
    private static readonly SKRectI WhereTheProductStands = new(700, 480, 840, 760);

    private static readonly SKRectI Whole = new(0, 0, 1080, 1920);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_Scene_of_generated_footage_is_its_Clip_from_edge_to_edge_with_no_Product_in_it()
    {
        var rendered = await RenderedAsync(app);
        var scenes = rendered.Storyboard.Scenes;
        var at = (scenes[0].DurationMs + scenes[1].DurationMs + scenes[2].DurationMs * 0.7) / 1000;

        using var locked = await FrameAsync(rendered.Worker, rendered.Locked.Mp4, at);
        using var hybrid = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, at);
        using var later = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, at + 1);

        Assert.Equal(Technique.GeneratedFootage, scenes[2].Technique);
        Assert.Equal(rendered.Clip.Id, scenes[2].ClipId);
        // Where Product Lock stands the Product on its panel, there is footage.
        var body = locked.GetPixel(770, 727);
        Assert.True(body.Blue > body.Red + 60, $"Product Lock shows {body} where the bottle's blue body should be.");
        Assert.True(Changed(locked, hybrid, WhereTheProductStands) > 0.5, "The Product is in the Scene of generated footage.");
        Assert.True(Changed(locked, hybrid, Whole) > 0.5, "The Clip does not fill the frame.");
        // It is footage, not a picture: it goes on moving once the Scene's type is in place.
        Assert.True(Changed(hybrid, later, Whole) > 0.01, "The footage does not move.");
    }

    [Fact]
    public async Task The_Scenes_type_is_set_over_the_footage()
    {
        var rendered = await RenderedAsync(app);
        var scenes = rendered.Storyboard.Scenes;
        var starts = (scenes[0].DurationMs + scenes[1].DurationMs) / 1000.0;

        using var first = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, starts + 0.1);
        using var settled = await FrameAsync(rendered.Worker, rendered.Hybrid.Mp4, starts + scenes[2].DurationMs * 0.7 / 1000);

        // The footage is under a wash, so nothing of it is white. What is white is the Fact, set in type.
        Assert.True(White(first) < 0.0005, $"There is white on the first frames of the footage: {White(first):P2}.");
        Assert.True(White(settled) > 0.005, $"The Fact is not set over the footage: {White(settled):P2} of the frame is white.");
    }

    [Fact]
    public async Task The_video_is_a_1080_by_1920_H264_and_AAC_MP4_as_long_as_its_Scenes()
    {
        var rendered = await RenderedAsync(app);
        await rendered.Worker.CopyAsync(rendered.Hybrid.Mp4, "/tmp/probe/footage.mp4", ct: Cancellation);

        var probe = await rendered.Worker.ExecAsync(
            ["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", "/tmp/probe/footage.mp4"], Cancellation);

        Assert.Equal(0, probe.ExitCode);
        using var described = JsonDocument.Parse(probe.Stdout);
        var streams = described.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Equal((1080, 1920), (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32()));
        Assert.Equal("h264", video.GetProperty("codec_name").GetString());
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal(15 * 30, int.Parse(video.GetProperty("nb_frames").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(15_000, rendered.Hybrid.Rendered.DurationMs);
        Assert.Equal(RenderedVideoState.ReadyForReview, rendered.Hybrid.Rendered.State);
    }

    [Fact]
    public async Task The_render_generates_nothing_and_a_Clip_is_paid_for_once_however_often_it_is_rendered()
    {
        var rendered = await RenderedAsync(app);

        Assert.Equal(RenderJobState.Completed, rendered.Hybrid.Job.State);
        // The Project has the Clips it had before anything was rendered, as they were approved, each with its one record.
        Assert.Equal(rendered.ClipsBeforeRendering.Items.Select(clip => clip.Id), rendered.ClipsAfterRendering.Items.Select(clip => clip.Id));
        Assert.All(rendered.ClipsAfterRendering.Items, clip => Assert.Equal(ClipState.Approved, clip.State));
        Assert.All(rendered.ClipsAfterRendering.Items, clip => Assert.Equal(clip.EstimatedAmount, clip.Cost?.EstimatedAmount));
        Assert.Equal(rendered.SpentBeforeRendering, rendered.SpentAfterRendering);
        // The same version rendered a second time draws nothing, and spends nothing.
        Assert.Empty(rendered.Again.Rendered.DrawnScenePositions);
    }

    [Fact]
    public async Task Only_the_generated_Scenes_and_the_Scene_after_the_footage_are_drawn_for_the_Hybrid_video()
    {
        var rendered = await RenderedAsync(app);

        Assert.Equal([1, 2, 3, 4], rendered.Locked.Rendered.DrawnScenePositions);
        // The reveal is what Product Lock drew. The closing follows a Scene that left no Product on screen, so it starts another way.
        Assert.Equal([1, 3, 4], rendered.Hybrid.Rendered.DrawnScenePositions);
    }

    [Fact]
    public async Task A_version_whose_generated_footage_has_no_approved_Clip_fails_validation_with_the_reason()
    {
        var rendered = await RenderedAsync(app);

        // The version from before the Clip was approved: its backdrop has its Still, and its footage refers to no Clip.
        Assert.NotNull(rendered.BeforeTheClip.Scenes[0].StillId);
        Assert.Null(rendered.BeforeTheClip.Scenes[2].ClipId);
        var job = rendered.WithoutTheClip;
        Assert.Equal(RenderJobState.Failed, job.State);
        Assert.Null(job.RenderedVideoId);
        Assert.Equal(
            "Scene 3 is generated footage and has no approved Clip. " +
            "Approve a Clip for it at Storyboard review, then render the version that makes.",
            job.Failure?.Message);
        Assert.Equal((RenderJobState.Validating, RenderFailureCategory.InvalidInput), (job.Failure!.Stage, job.Failure.Category));
        Assert.Equal(1, job.Attempt);
    }

    [Fact]
    public async Task Problem_Solution_states_its_problem_over_footage_and_sets_its_Facts_over_footage_too()
    {
        var rendered = await RenderedAsync(app);
        var scenes = rendered.ProblemStoryboard.Scenes;
        var inHook = scenes[0].DurationMs * 0.8 / 1000;
        var inFacts = (scenes[0].DurationMs + scenes[1].DurationMs + scenes[2].DurationMs * 0.8) / 1000;

        using var hook = await FrameAsync(rendered.Worker, rendered.Problem.Mp4, inHook);
        using var hookEarlier = await FrameAsync(rendered.Worker, rendered.Problem.Mp4, inHook - 0.5);
        using var facts = await FrameAsync(rendered.Worker, rendered.Problem.Mp4, inFacts);
        using var factsEarlier = await FrameAsync(rendered.Worker, rendered.Problem.Mp4, inFacts - 1);

        Assert.Equal(
            [Technique.GeneratedFootage, Technique.ImageMotion, Technique.GeneratedFootage, Technique.ImageMotion],
            scenes.Select(scene => scene.Technique));
        Assert.All(new[] { scenes[0], scenes[2] }, scene => Assert.NotNull(scene.ClipId));
        Assert.NotEqual(scenes[0].ClipId, scenes[2].ClipId);
        // Both Scenes are moving footage under type that has arrived and stays.
        Assert.True(Changed(hook, hookEarlier, Whole) > 0.01, "The problem is not stated over moving footage.");
        Assert.True(Changed(facts, factsEarlier, Whole) > 0.01, "The Facts are not set over moving footage.");
        Assert.True(White(hook) > 0.005, $"The Hook is not set over the footage: {White(hook):P2} of the frame is white.");
        Assert.True(White(facts) > 0.002, $"The Fact is not set over the footage: {White(facts):P2} of the frame is white.");
        Assert.Equal([1, 2, 3, 4], rendered.Problem.Rendered.DrawnScenePositions);
    }

    [Fact]
    public async Task Luxury_Cinematic_sets_its_Facts_over_footage_behind_a_Hook_on_a_generated_backdrop()
    {
        var rendered = await RenderedAsync(app);
        var scenes = rendered.LuxuryStoryboard.Scenes;
        var inFacts = (scenes[0].DurationMs + scenes[1].DurationMs * 0.8) / 1000;

        using var facts = await FrameAsync(rendered.Worker, rendered.Luxury.Mp4, inFacts);
        using var factsEarlier = await FrameAsync(rendered.Worker, rendered.Luxury.Mp4, inFacts - 1);

        Assert.Equal([Technique.GeneratedBackdrop, Technique.GeneratedFootage, Technique.ImageMotion], scenes.Select(scene => scene.Technique));
        Assert.NotNull(scenes[0].StillId);
        Assert.NotNull(scenes[1].ClipId);
        Assert.True(Changed(facts, factsEarlier, Whole) > 0.01, "The Facts are not set over moving footage.");
        Assert.True(White(facts) > 0.001, $"The Fact is not set over the footage: {White(facts):P2} of the frame is white.");
        Assert.Equal([1, 2, 3], rendered.Luxury.Rendered.DrawnScenePositions);
    }

    [Fact]
    public async Task Withdrawing_the_Fact_the_footage_rests_on_flags_the_versions_and_the_videos_that_used_the_Clip()
    {
        var rendered = await RenderedAsync(app);

        // Nothing was flagged while the Fact was Confirmed.
        Assert.Empty(rendered.Storyboard.Flags);
        Assert.Empty(rendered.Hybrid.Rendered.Flags);
        // Every version whose Scene refers to the Clip, and each video rendered from one.
        Assert.Contains(rendered.VersionsAfterWithdrawal, version => version.Scenes[2].ClipId == rendered.Clip.Id);
        Assert.All(
            rendered.VersionsAfterWithdrawal.Where(version => version.Scenes[2].ClipId == rendered.Clip.Id),
            version => Assert.Equal([rendered.Fact.Id], version.Flags.Select(flag => flag.FactId)));
        Assert.Equal([rendered.Fact.Id], rendered.HybridAfterWithdrawal.Flags.Select(flag => flag.FactId));
        Assert.Equal(rendered.Fact.Text, rendered.HybridAfterWithdrawal.Flags[0].Text);
        // A version planned now rests on no Withdrawn Fact, so it does not have that Clip.
        Assert.NotNull(rendered.PlannedAfterWithdrawal);
        Assert.All(rendered.PlannedAfterWithdrawal.Scenes, scene => Assert.NotEqual(rendered.Clip.Id, scene.ClipId));
    }

    /// <summary>
    /// One Product, with a photo that came already cut out and two Confirmed Facts, in one 15-second
    /// Project of four Variants: Product Showcase in Product Lock and in Hybrid, and Problem–Solution
    /// and Luxury Cinematic in Hybrid. One worker with both fake providers made the Stills and the Clips, each approved in
    /// turn, and rendered them. Last of all the Fact the footage rests on was withdrawn.
    /// </summary>
    internal static Task<FootageRendered> RenderedAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        var stack = await app.NewStackAsync(FakeFootage(StillRate, ClipRate));
        var member = await stack.NewMemberAsync();
        var product = await NewProductAsync(member, "Bình giữ nhiệt Lumo 500");
        await ProductAssetTests.UploadedAsync(member, product, ProductAssetKind.Photo, RenderTests.Bottle(onBackdrop: false));
        var fact = await ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        // A second, so that a Storyboard can still be planned once the first is withdrawn.
        await ConfirmedFactAsync(member, product, "Không chứa BPA");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { TargetDurationSeconds = 15 });
        var locked = await VariantTests.AddAsync(member, project.Id);
        var hybrid = await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        var problem = await VariantTests.AddAsync(
            member, project.Id, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?", RenderMode.Hybrid));
        var luxury = await VariantTests.AddAsync(
            member, project.Id, new VariantRequest(CreativeTemplate.LuxuryCinematic, VariantTests.Valid().Hook, RenderMode.Hybrid));
        await GeneratedAsync(member, locked);
        await GeneratedAsync(member, hybrid);
        await GeneratedAsync(member, problem);
        await GeneratedAsync(member, luxury);

        var worker = await stack.StartWorkerAsync(FakeFootageWorker);
        var clip = Assert.Single(await MadeAndApprovedAsync(member, hybrid, worker));
        await MadeAndApprovedAsync(member, problem, worker);
        await MadeAndApprovedAsync(member, luxury, worker);
        var luxuryStoryboard = (await ListAsync(member, luxury)).Items[0];
        var storyboard = (await ListAsync(member, hybrid)).Items[0];
        var beforeTheClip = await member.GetAsync<StoryboardResponse>($"{Storyboards(hybrid)}/{storyboard.Version - 1}");
        var problemStoryboard = (await ListAsync(member, problem)).Items[0];
        var clipsBefore = await ClipsAsync(member, hybrid);
        var spentBefore = (await member.GetAsync<GenerationEstimateResponse>(StillGenerationTests.Generation(hybrid, storyboard.Version))).Spent;

        var lockedVideo = await VideoAsync(member, worker, locked, 1);
        var hybridVideo = await VideoAsync(member, worker, hybrid, storyboard.Version);
        var problemVideo = await VideoAsync(member, worker, problem, problemStoryboard.Version);
        var luxuryVideo = await VideoAsync(member, worker, luxury, luxuryStoryboard.Version);
        var again = await VideoAsync(member, worker, hybrid, storyboard.Version);
        var withoutTheClip = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, hybrid, beforeTheClip.Version));
        var clipsAfter = await ClipsAsync(member, hybrid);
        var spentAfter = (await member.GetAsync<GenerationEstimateResponse>(StillGenerationTests.Generation(hybrid, storyboard.Version))).Spent;

        (await FactTests.WithdrawAsync(member, product, fact.Id)).EnsureSuccessStatusCode();
        var versions = (await ListAsync(member, hybrid)).Items;
        var hybridAfter = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{hybridVideo.Rendered.Id}");
        var planned = await GeneratedAsync(member, hybrid);

        return new FootageRendered(
            worker, fact, storyboard, beforeTheClip, clip, lockedVideo, hybridVideo, again, withoutTheClip, problemStoryboard, problemVideo,
            luxuryStoryboard, luxuryVideo,
            clipsBefore, clipsAfter, spentBefore, spentAfter, versions, hybridAfter, planned);
    });

    // Confirms the estimate of the Variant's first version once, approves each Still as it is made, and
    // then each Clip, which that started. What comes back is the Clips, approved.
    private static async Task<IReadOnlyList<ClipResponse>> MadeAndApprovedAsync(Browser member, VariantResponse variant, IContainer worker)
    {
        var confirmed = await ConfirmedAsync(member, variant, 1);
        foreach (var queued in confirmed.Stills)
        {
            var still = await StillGenerationTests.EndedAsync(member, queued, worker);
            Assert.True(still.State == StillState.ReadyForReview, $"The Still ended {still.State}: {still.Failure?.Message}");
            (await member.PostAsync($"/api/v1/stills/{still.Id}/approve", new { })).EnsureSuccessStatusCode();
        }

        var clips = new List<ClipResponse>();
        foreach (var waiting in confirmed.Clips)
        {
            var clip = await ClipEndedAsync(member, waiting, worker);
            Assert.True(clip.State == ClipState.ReadyForReview, $"The Clip ended {clip.State}: {clip.Failure?.Message}\n{await RenderStack.LogAsync(worker)}");
            var approval = await member.PostAsync($"/api/v1/clips/{clip.Id}/approve", new { });
            approval.EnsureSuccessStatusCode();
            clips.Add((await approval.Content.ReadFromJsonAsync<ClipResponse>(AffiVideoApp.Json, Cancellation))!);
        }
        return clips;
    }

    // The share of the frame that is white: brighter than anything under a wash can be.
    private static double White(SKBitmap frame)
    {
        var white = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var pixel = frame.GetPixel(x, y);
                if (pixel.Red > 235 && pixel.Green > 235 && pixel.Blue > 235) white++;
            }
        }
        return (double)white / (frame.Width * frame.Height);
    }

    /// <param name="Worker">The worker that made and rendered everything, still running: FFmpeg is there.</param>
    /// <param name="Fact">The Fact the footage rests on, withdrawn once everything was rendered.</param>
    /// <param name="Storyboard">The Hybrid Product Showcase version that refers to the Clip, as it was before the Fact was withdrawn.</param>
    /// <param name="BeforeTheClip">The version before it: its backdrop has its Still, and its footage no Clip yet.</param>
    /// <param name="Clip">The Clip of the Hybrid Product Showcase Variant's Facts Scene.</param>
    /// <param name="Again"><paramref name="Storyboard"/> rendered a second time.</param>
    /// <param name="WithoutTheClip">The job that was to render <paramref name="BeforeTheClip"/>.</param>
    /// <param name="VersionsAfterWithdrawal">Every version of the Hybrid Product Showcase Variant, once the Fact was withdrawn.</param>
    /// <param name="PlannedAfterWithdrawal">The version planned after that.</param>
    internal sealed record FootageRendered(
        IContainer Worker, FactResponse Fact, StoryboardResponse Storyboard, StoryboardResponse BeforeTheClip, ClipResponse Clip,
        Video Locked, Video Hybrid, Video Again, RenderJobResponse WithoutTheClip, StoryboardResponse ProblemStoryboard, Video Problem,
        StoryboardResponse LuxuryStoryboard, Video Luxury,
        PagedResponse<ClipResponse> ClipsBeforeRendering, PagedResponse<ClipResponse> ClipsAfterRendering,
        decimal SpentBeforeRendering, decimal SpentAfterRendering,
        IReadOnlyList<StoryboardResponse> VersionsAfterWithdrawal, RenderedVideoResponse HybridAfterWithdrawal,
        StoryboardResponse PlannedAfterWithdrawal);
}
