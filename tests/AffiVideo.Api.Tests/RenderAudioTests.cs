using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>
/// How uploaded narration and music sound in a Rendered Video: the worker renders
/// Variants that differ only in their audio, and the audio of the MP4 the API then
/// serves is measured with FFmpeg and ffprobe in the worker's container.
/// </summary>
public sealed partial class RenderAudioTests(AffiVideoApp app)
{
    // A steady tone brought to the target loudness, as volumedetect measures it: the mean
    // level of a sine at -16 LUFS in both channels is 18.3 dB below full scale.
    private const double TargetMeanDb = -18.3;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_video_with_narration_and_music_has_an_AAC_track_as_long_as_the_video_and_it_is_not_silent()
    {
        var mixed = await MixedAsync(app);
        var both = mixed.NarrationAndMusic;

        var probe = await app.InWorkerAsync(
            ["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", "/tmp/probe/both.mp4"],
            ("/tmp/probe/both.mp4", both.Mp4));
        var level = await LevelAsync(both.Mp4, from: 0, seconds: 15);

        Assert.Equal(0, probe.ExitCode);
        using var described = JsonDocument.Parse(probe.Stdout);
        var streams = described.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("aac", audio.GetProperty("codec_name").GetString());
        Assert.Equal(2, audio.GetProperty("channels").GetInt32());
        Assert.Equal("48000", audio.GetProperty("sample_rate").GetString());
        Assert.Equal(15_000, both.Video.DurationMs);
        Assert.InRange(Seconds(audio.GetProperty("duration")), 14.95, 15.05);
        Assert.InRange(Seconds(described.RootElement.GetProperty("format").GetProperty("duration")), 14.95, 15.05);
        Assert.True(level.Max > -20, $"The loudest the audio gets is {level.Max} dB.");
        Assert.True(level.Mean > -25, $"The audio's mean level is {level.Mean} dB.");
    }

    [Fact]
    public async Task A_Rendered_Video_says_which_narration_and_music_were_mixed_into_it_and_how_loud_the_music()
    {
        var mixed = await MixedAsync(app);
        var silent = await RenderTests.RenderedAsync(app);

        var both = mixed.NarrationAndMusic;
        Assert.NotNull(both.Narration);
        Assert.NotNull(both.Music);
        Assert.Equal(
            (both.Narration.Id, both.Music.Id, VariantAudio.DefaultMusicVolumePercent),
            (both.Video.NarrationAudioId, both.Video.MusicAudioId, both.Video.MusicVolumePercent));
        Assert.Equal((null, mixed.MusicAtAQuarter.Music!.Id, 25), (
            mixed.MusicAtAQuarter.Video.NarrationAudioId, mixed.MusicAtAQuarter.Video.MusicAudioId, mixed.MusicAtAQuarter.Video.MusicVolumePercent));
        Assert.Equal((null, null, null), (silent.Video.NarrationAudioId, silent.Video.MusicAudioId, silent.Video.MusicVolumePercent));
    }

    [Fact]
    public async Task Narration_is_brought_to_the_same_loudness_whether_it_was_recorded_quietly_or_loudly()
    {
        var mixed = await MixedAsync(app);

        // One was uploaded at a fiftieth of full scale and the other at nine tenths: 33 dB apart.
        var quiet = await LevelAsync(mixed.QuietShortNarration.Mp4, from: 1, seconds: 2);
        var loud = await LevelAsync(mixed.LoudLongNarration.Mp4, from: 5, seconds: 5);

        Assert.InRange(quiet.Mean, TargetMeanDb - 2, TargetMeanDb + 2);
        Assert.InRange(loud.Mean, TargetMeanDb - 2, TargetMeanDb + 2);
        Assert.InRange(quiet.Mean - loud.Mean, -1.5, 1.5);
    }

    [Fact]
    public async Task Narration_and_music_are_both_in_the_mix_the_music_under_the_narration_by_its_volume()
    {
        var mixed = await MixedAsync(app);
        var both = mixed.NarrationAndMusic;
        Assert.Equal(VariantAudio.DefaultMusicVolumePercent, both.Music!.VolumePercent);

        // The narration is a tone of 440 Hz and the music one of 880 Hz: each is listened for alone.
        var narration = await LevelAsync(both.Mp4, from: 5, seconds: 5, onlyAroundHz: 440);
        var music = await LevelAsync(both.Mp4, from: 5, seconds: 5, onlyAroundHz: 880);

        // Three tenths of the narration's level is 10.5 dB under it.
        Assert.InRange(narration.Mean, TargetMeanDb - 2.5, TargetMeanDb + 2.5);
        Assert.InRange(narration.Mean - music.Mean, 10.46 - 2, 10.46 + 2);
    }

    [Fact]
    public async Task Narration_whose_peaks_are_far_above_its_loudness_is_still_brought_to_the_same_loudness()
    {
        var mixed = await MixedAsync(app);

        // A tone at three hundredths of full scale, 33.5 dB down, with a click at full scale every second. A gain
        // that kept the clicks whole could not turn it up at all. The clicks are part of how loud it is, so
        // the tone alone ends a few dB under where a tone with no clicks would.
        var tone = await LevelAsync(mixed.QuietNarrationWithLoudClicks.Mp4, from: 5, seconds: 5, onlyAroundHz: 440);
        var whole = await LevelAsync(mixed.QuietNarrationWithLoudClicks.Mp4, from: 0, seconds: 15);

        Assert.InRange(tone.Mean, TargetMeanDb - 6, TargetMeanDb + 2);
        Assert.True(whole.Max <= -1, $"The loudest the audio gets is {whole.Max} dB.");
    }

    [Fact]
    public async Task Music_is_mixed_at_the_volume_the_member_chose()
    {
        var mixed = await MixedAsync(app);

        var full = await LevelAsync(mixed.MusicAtFull.Mp4, from: 5, seconds: 5);
        var quarter = await LevelAsync(mixed.MusicAtAQuarter.Mp4, from: 5, seconds: 5);
        var none = await LevelAsync(mixed.MusicAtNone.Mp4, from: 0, seconds: 15);

        // At 100 music is as loud as narration is; a quarter of that is 12 dB less; at 0 it is not heard.
        Assert.InRange(full.Mean, TargetMeanDb - 2, TargetMeanDb + 2);
        Assert.InRange(full.Mean - quarter.Mean, 12.04 - 1.5, 12.04 + 1.5);
        Assert.True(none.Max <= -90, $"Music at no volume is heard at {none.Max} dB.");
        Assert.Equal(0, mixed.MusicAtNone.Video.MusicVolumePercent);
    }

    [Fact]
    public async Task Audio_longer_than_the_video_is_cut_where_the_video_ends_and_fades_out()
    {
        var mixed = await MixedAsync(app);
        var video = mixed.LoudLongNarration;
        Assert.Equal(20_000, video.Narration!.DurationMs);

        var duration = await DurationAsync(video.Mp4);
        var middle = await LevelAsync(video.Mp4, from: 5, seconds: 5);
        var beforeTheFade = await LevelAsync(video.Mp4, from: 13, seconds: 1);
        var lastTenth = await LevelAsync(video.Mp4, from: 14.9, seconds: 0.1);

        Assert.InRange(duration, 14.95, 15.05);
        Assert.InRange(beforeTheFade.Mean - middle.Mean, -1, 1);
        Assert.True(lastTenth.Mean < middle.Mean - 12, $"The last tenth of a second is at {lastTenth.Mean} dB, the middle at {middle.Mean} dB.");
    }

    [Fact]
    public async Task Audio_shorter_than_the_video_does_not_shorten_it_and_is_followed_by_silence()
    {
        var mixed = await MixedAsync(app);
        var video = mixed.QuietShortNarration;
        Assert.Equal(4_000, video.Narration!.DurationMs);

        var duration = await DurationAsync(video.Mp4);
        var whileItLasts = await LevelAsync(video.Mp4, from: 1, seconds: 2);
        var after = await LevelAsync(video.Mp4, from: 6, seconds: 8);

        Assert.Equal(15_000, video.Video.DurationMs);
        Assert.InRange(duration, 14.95, 15.05);
        Assert.True(whileItLasts.Max > -25, $"The narration is heard at {whileItLasts.Max} dB.");
        Assert.True(after.Max <= -70, $"After the narration has ended the audio is at {after.Max} dB.");
    }

    [Fact]
    public async Task A_video_keeps_its_sound_when_its_audio_is_removed_from_the_Variant_afterwards()
    {
        var mixed = await MixedAsync(app);
        using var member = await app.SignedInAsync(mixed.Owner);
        var video = mixed.MusicAtFull;

        (await member.DeleteAsync($"{VariantAudioTests.Audio(video.Variant)}/{video.Music!.Id}")).EnsureSuccessStatusCode();

        var after = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{video.Video.Id}");
        var content = await member.Http.GetByteArrayAsync($"/api/v1/rendered-videos/{video.Video.Id}/content", Cancellation);
        Assert.Equal(video.Music.Id, after.MusicAudioId);
        Assert.Equal(video.Mp4, content);
    }

    /// <summary>
    /// Variants of one Project in an Organization of their own, each with the same creative template
    /// and Hook, and so the same Scenes: the worker draws them for the first and for none after, and
    /// the videos differ only in what was uploaded to be heard. Every sound is a steady tone.
    /// </summary>
    internal static Task<Mixed> MixedAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        await app.WorkerAsync();
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, "Giữ lạnh suốt 24 giờ");
        var first = await StoryboardTests.NewVariantAsync(member, product, targetDurationSeconds: 15);

        async Task<RenderedWithAudio> RenderAsync(byte[]? narration, byte[]? music, string? musicVolume = null)
        {
            var variant = await VariantTests.AddAsync(member, first.ProjectId, new VariantRequest(first.CreativeTemplate, first.Hook));
            var storyboard = await StoryboardTests.GeneratedAsync(member, variant);
            var narrationAudio = narration is null
                ? null
                : await VariantAudioTests.UploadedAsync(member, variant, VariantAudioKind.Narration, narration);
            var musicAudio = music is null
                ? null
                : await VariantAudioTests.UploadedAsync(member, variant, VariantAudioKind.Music, music, musicVolume);

            var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, variant, storyboard.Version));
            Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{job.Failure?.Detail}\n{await app.WorkerLogAsync()}");
            var video = await member.GetAsync<RenderedVideoResponse>($"/api/v1/rendered-videos/{job.RenderedVideoId}");
            var mp4 = await member.Http.GetByteArrayAsync($"/api/v1/rendered-videos/{video.Id}/content", Cancellation);
            return new RenderedWithAudio(variant, narrationAudio, musicAudio, video, mp4);
        }

        var music = VariantAudioTests.Tone(seconds: 20, amplitude: 0.5, frequency: 220, channels: 2);
        return new Mixed(
            organization.Owner,
            NarrationAndMusic: await RenderAsync(
                VariantAudioTests.Tone(seconds: 15, amplitude: 0.3), VariantAudioTests.Tone(seconds: 15, amplitude: 0.3, frequency: 880, channels: 2)),
            QuietShortNarration: await RenderAsync(VariantAudioTests.Tone(seconds: 4, amplitude: 0.02), null),
            QuietNarrationWithLoudClicks: await RenderAsync(WithClicks(VariantAudioTests.Tone(seconds: 15, amplitude: 0.03)), null),
            LoudLongNarration: await RenderAsync(VariantAudioTests.Tone(seconds: 20, amplitude: 0.9, sampleRate: 22050), null),
            MusicAtFull: await RenderAsync(null, music, "100"),
            MusicAtAQuarter: await RenderAsync(null, music, "25"),
            MusicAtNone: await RenderAsync(null, music, "0"));
    });

    internal sealed record Mixed(
        Credentials Owner,
        RenderedWithAudio NarrationAndMusic,
        RenderedWithAudio QuietShortNarration,
        RenderedWithAudio QuietNarrationWithLoudClicks,
        RenderedWithAudio LoudLongNarration,
        RenderedWithAudio MusicAtFull,
        RenderedWithAudio MusicAtAQuarter,
        RenderedWithAudio MusicAtNone);

    /// <summary>A Variant, what was uploaded for it, and the video rendered from it.</summary>
    internal sealed record RenderedWithAudio(
        VariantResponse Variant, VariantAudioResponse? Narration, VariantAudioResponse? Music, RenderedVideoResponse Video, byte[] Mp4);

    // A mono 16-bit 44.1 kHz WAV with a click as loud as a file can hold, a quarter of a millisecond long, every second.
    private static byte[] WithClicks(byte[] wav)
    {
        for (var second = 0; second < 15; second++)
        {
            for (var sample = 0; sample < 11; sample++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                    wav.AsSpan(44 + (second * 44100 + 22050 + sample) * 2), sample % 2 == 0 ? short.MaxValue : short.MinValue);
            }
        }
        return wav;
    }

    // How loud a stretch of the video's audio is, in dB below full scale: its mean level and its highest sample.
    private async Task<(double Mean, double Max)> LevelAsync(byte[] mp4, double from, double seconds, int? onlyAroundHz = null)
    {
        var file = $"/tmp/probe/sound-{Guid.NewGuid():N}.mp4";
        var listened = await app.InWorkerAsync(
            [
                "ffmpeg", "-hide_banner", "-ss", from.ToString(CultureInfo.InvariantCulture), "-t", seconds.ToString(CultureInfo.InvariantCulture),
                "-i", file, "-vn", "-af", onlyAroundHz is { } hz ? $"bandpass=f={hz}:width_type=h:w=60,volumedetect" : "volumedetect",
                "-f", "null", "-",
            ],
            (file, mp4));
        Assert.True(listened.ExitCode == 0, listened.Stderr);
        return (Level(MeanVolume().Match(listened.Stderr)), Level(MaxVolume().Match(listened.Stderr)));

        double Level(Match found)
        {
            Assert.True(found.Success, listened.Stderr);
            var level = found.Groups[1].Value;
            return level == "-inf" ? double.NegativeInfinity : double.Parse(level, CultureInfo.InvariantCulture);
        }
    }

    private async Task<double> DurationAsync(byte[] mp4)
    {
        var file = $"/tmp/probe/length-{Guid.NewGuid():N}.mp4";
        var probe = await app.InWorkerAsync(
            ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json", file], (file, mp4));
        Assert.True(probe.ExitCode == 0, probe.Stderr);
        using var described = JsonDocument.Parse(probe.Stdout);
        return Seconds(described.RootElement.GetProperty("format").GetProperty("duration"));
    }

    private static double Seconds(JsonElement text) => double.Parse(text.GetString()!, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"mean_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MeanVolume();

    [GeneratedRegex(@"max_volume: (-?[\d.]+|-inf) dB")]
    private static partial Regex MaxVolume();
}
