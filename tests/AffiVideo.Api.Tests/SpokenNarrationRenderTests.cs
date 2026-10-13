using System.Globalization;
using System.Text.RegularExpressions;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using static AffiVideo.Api.Tests.HybridRenderTests;
using static AffiVideo.Api.Tests.SpokenNarrationTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// How speech made from narration text sounds in a Rendered Video: the worker
/// renders a Variant whose every Scene has been spoken by the fake speech
/// provider, and the audio of the MP4 the API then serves is measured with FFmpeg
/// in the worker's container. The fake's speech is a 220 Hz tone as long as the
/// text is estimated to take, so where it is heard says where the speech was put.
/// </summary>
public sealed partial class SpokenNarrationRenderTests(AffiVideoApp app)
{
    private const int ToneHz = 220;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    // The 20-second Product Showcase: the Hook from 0, the reveal from 3, the Facts from 8 and the closing from 15.
    // The Hook's five words take 1.25 seconds to say, and the rest of its Scene is silent.
    [InlineData(0.1, 1.0, true)]
    [InlineData(1.6, 1.2, false)]
    // "Đây là Lumo 500.": four words, a second.
    [InlineData(3.1, 0.8, true)]
    [InlineData(4.4, 3.4, false)]
    // The one Fact: six words, a second and a half.
    [InlineData(8.1, 1.3, true)]
    [InlineData(9.9, 4.9, false)]
    // The closing: ten words and a pause between its two sentences, 2.8 seconds.
    [InlineData(15.1, 2.6, true)]
    [InlineData(18.2, 1.6, false)]
    public async Task Each_Scenes_speech_starts_with_its_Scene_and_the_rest_of_the_Scene_is_silent(double from, double seconds, bool spoken)
    {
        var rendered = await SpokenAsync(app);

        var level = await LevelAsync(rendered.Worker, rendered.Video.Mp4, from, seconds, onlyAroundHz: ToneHz);

        if (spoken)
        {
            Assert.True(level.Mean > -30, $"From {from} for {seconds} seconds the tone's mean level is {level.Mean} dB.");
        }
        else
        {
            Assert.True(level.Max < -60, $"From {from} for {seconds} seconds the loudest the tone gets is {level.Max} dB.");
        }
    }

    [Fact]
    public async Task Speech_is_brought_to_the_same_loudness_as_an_uploaded_narration_is()
    {
        var rendered = await SpokenAsync(app);

        var level = await LevelAsync(rendered.Worker, rendered.Video.Mp4, from: 8.1, seconds: 1.3);

        // A steady tone at -16 LUFS in both channels has a mean level 18.3 dB below full scale, however loud the provider made it.
        Assert.InRange(level.Mean, -20.3, -16.3);
    }

    [Fact]
    public async Task A_version_with_a_Scene_whose_narration_text_has_not_been_spoken_is_not_rendered_and_the_reason_names_the_Scene()
    {
        var rendered = await SpokenAsync(app);
        var member = rendered.Member;
        var first = await member.GetAsync<StoryboardResponse>($"{Storyboards(rendered.Variant)}/1");
        // The reveal is given other words, which nobody has confirmed speech for.
        var edited = await StoryboardEditTests.EditedAsync(
            member, rendered.Variant, 1, StoryboardEditTests.Changing(first, new SceneEditRequest(2, NarrationText: "Gặp Lumo 500")));

        var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, rendered.Variant, edited.Version));

        Assert.Equal(RenderJobState.Failed, job.State);
        Assert.NotNull(job.Failure);
        Assert.Contains("Scene 2", job.Failure.Message);
        Assert.Contains("has not been spoken", job.Failure.Message);
        // One attempt: a render that waits for the member is not tried again.
        Assert.Equal(1, job.Attempt);
        Assert.Null(job.RenderedVideoId);
    }

    [Fact]
    public async Task An_uploaded_narration_is_used_in_place_of_the_speech()
    {
        var rendered = await SpokenAsync(app);
        var member = rendered.Member;
        // Another Variant of the Project with the same Hook: every Scene of it already has its speech.
        var twin = await VariantTests.AddAsync(member, rendered.Variant.ProjectId);
        await GeneratedAsync(member, twin);
        // Three seconds at 440 Hz, which ends where the reveal starts.
        var upload = await VariantAudioTests.UploadedAsync(member, twin, VariantAudioKind.Narration, VariantAudioTests.Tone(seconds: 3));

        var video = await VideoAsync(member, rendered.Worker, twin, 1);
        var uploaded = await LevelAsync(rendered.Worker, video.Mp4, from: 0.3, seconds: 2.4, onlyAroundHz: 440);
        var facts = await LevelAsync(rendered.Worker, video.Mp4, from: 8.1, seconds: 1.3, onlyAroundHz: ToneHz);

        Assert.Equal(upload.Id, video.Rendered.NarrationAudioId);
        Assert.True(uploaded.Mean > -30, $"The uploaded tone's mean level is {uploaded.Mean} dB.");
        // Where the Facts Scene's speech would be, nothing is said.
        Assert.True(facts.Max < -60, $"The loudest the speech's tone gets is {facts.Max} dB.");
    }

    [Fact]
    public async Task Speech_a_little_longer_than_its_Scene_is_sped_up_to_end_with_it()
    {
        var rendered = await SpokenAsync(app);
        var member = rendered.Member;
        var first = await member.GetAsync<StoryboardResponse>($"{Storyboards(rendered.Variant)}/1");
        // The Facts Scene lasts 7 seconds, from 8 to 15. Thirty words take 7.5 to say: played as they are they would run
        // half a second into the closing, which is given nothing to say so that the half second can be heard.
        var thirtyWords = string.Join(" ", Enumerable.Repeat("một", 30));
        var edited = await StoryboardEditTests.EditedAsync(
            member, rendered.Variant, 1,
            StoryboardEditTests.Changing(first, new SceneEditRequest(3, NarrationText: thirtyWords), new SceneEditRequest(4, NarrationText: " ")));
        var speech = await EndedAsync(member, Assert.Single(await ConfirmedSpeechAsync(member, rendered.Variant, edited.Version)), rendered.Worker);

        var video = await VideoAsync(member, rendered.Worker, rendered.Variant, edited.Version);
        var end = await LevelAsync(rendered.Worker, video.Mp4, from: 14.3, seconds: 0.5, onlyAroundHz: ToneHz);
        var after = await LevelAsync(rendered.Worker, video.Mp4, from: 15.15, seconds: 0.3, onlyAroundHz: ToneHz);

        Assert.Equal(7500, speech.DurationMs);
        Assert.True(end.Mean > -30, $"Just before the Scene ends the tone's mean level is {end.Mean} dB.");
        Assert.True(after.Max < -60, $"Just after the Scene ends the loudest the tone gets is {after.Max} dB.");
    }

    [Fact]
    public async Task Speech_much_longer_than_its_Scene_is_not_rendered_and_the_reason_names_the_Scene()
    {
        var rendered = await SpokenAsync(app);
        var member = rendered.Member;
        var first = await member.GetAsync<StoryboardResponse>($"{Storyboards(rendered.Variant)}/1");
        // The Hook's Scene lasts 3 seconds, and twenty words take 5 to say.
        var twentyWords = string.Join(" ", Enumerable.Repeat("hai", 20));
        var edited = await StoryboardEditTests.EditedAsync(
            member, rendered.Variant, 1, StoryboardEditTests.Changing(first, new SceneEditRequest(1, NarrationText: twentyWords)));
        await EndedAsync(member, Assert.Single(await ConfirmedSpeechAsync(member, rendered.Variant, edited.Version)), rendered.Worker);

        var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, rendered.Variant, edited.Version));

        Assert.Equal(RenderJobState.Failed, job.State);
        Assert.NotNull(job.Failure);
        Assert.Contains("Scene 1 lasts 5 seconds and the Scene 3", job.Failure.Message);
        Assert.Equal(1, job.Attempt);
    }

    /// <param name="Worker">A worker that is still running, where FFmpeg is.</param>
    /// <param name="Video">The 20-second video of a Product Lock Variant whose four Scenes were spoken.</param>
    internal sealed record Spoken(Browser Member, IContainer Worker, VariantResponse Variant, Video Video);

    /// <summary>One Variant spoken and rendered, for every test that only listens to the result.</summary>
    internal static Task<Spoken> SpokenAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        var stack = await app.NewStackAsync(FakeSpeech(ratePerCharacter: 0.001m));
        var member = await stack.NewMemberAsync();
        var variant = await SpokenVariantAsync(member);
        await GeneratedAsync(member, variant);
        var worker = await stack.StartWorkerAsync(FakeSpeechWorker);
        foreach (var speech in await ConfirmedSpeechAsync(member, variant, 1))
        {
            var ended = await EndedAsync(member, speech, worker);
            Assert.True(ended.State == SpeechState.Made, $"The speech ended {ended.State}: {ended.Failure?.Message}");
        }

        return new Spoken(member, worker, variant, await VideoAsync(member, worker, variant, 1));
    });

    // How loud a part of the video's audio is, measured by FFmpeg in a worker's container.
    private static async Task<(double Mean, double Max)> LevelAsync(IContainer worker, byte[] mp4, double from, double seconds, int? onlyAroundHz = null)
    {
        var file = $"/tmp/probe/spoken-{Guid.NewGuid():N}.mp4";
        await worker.CopyAsync(mp4, file, ct: Cancellation);
        var listened = await worker.ExecAsync(
            [
                "ffmpeg", "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture), "-t", seconds.ToString(CultureInfo.InvariantCulture),
                "-i", file, "-vn", "-af", onlyAroundHz is { } hz ? $"bandpass=f={hz}:width_type=h:w=60,volumedetect" : "volumedetect",
                "-f", "null", "-",
            ],
            Cancellation);
        Assert.True(listened.ExitCode == 0, listened.Stderr);
        return (Level(MeanVolume().Match(listened.Stderr)), Level(MaxVolume().Match(listened.Stderr)));

        double Level(Match found)
        {
            Assert.True(found.Success, listened.Stderr);
            var level = found.Groups[1].Value;
            return level == "-inf" ? double.NegativeInfinity : double.Parse(level, CultureInfo.InvariantCulture);
        }
    }

    [GeneratedRegex(@"mean_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MeanVolume();

    [GeneratedRegex(@"max_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MaxVolume();
}
