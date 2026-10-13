using System.Globalization;
using System.Text.Json;
using AffiVideo.Domain;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Joins the Scene clips, adds the audio and checks the result (ADR 0002). No
/// text a member typed reaches FFmpeg: it is given files the worker named itself.
/// </summary>
internal sealed class Ffmpeg(IOptions<RenderingOptions> options)
{
    /// <summary>How far the finished file's duration may be from the sum of its Scenes.</summary>
    public const double DurationToleranceSeconds = 0.1;

    /// <summary>What every track is brought to before it is mixed: stereo, at the sample rate of the video's audio.</summary>
    private const string Conformed = "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo";

    private readonly RenderingOptions _options = options.Value;

    /// <summary>
    /// Joins the clips in order, without encoding the video a second time, under
    /// the audio: each track brought to one loudness, the music turned down to its
    /// volume, and the two mixed. A track that is longer than the video is cut
    /// where the video ends, fading out; one that is shorter is followed by
    /// silence, so the audio is always exactly as long as the video. With no track
    /// at all the audio is silence: a video with no narration still has an audio
    /// track, so it plays everywhere.
    /// </summary>
    /// <param name="tracks">The narration and the music, each a WAV file this system wrote. None, one or both.</param>
    public async Task JoinAsync(
        string jobDirectory, IReadOnlyList<string> clips, IReadOnlyList<AudioTrack> tracks, int durationMs, string output,
        CancellationToken cancellationToken)
    {
        var listing = Path.Combine(jobDirectory, "clips.txt");
        await File.WriteAllLinesAsync(listing, clips.Select(clip => $"file '{clip.Replace("'", @"'\''")}'"), cancellationToken);
        var seconds = Seconds(durationMs);

        List<string> arguments = ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", listing];
        // Music at no volume is not heard, and so is not mixed.
        var heard = tracks.Where(track => track.VolumePercent > 0).ToList();
        if (heard.Count == 0)
        {
            arguments.AddRange(["-f", "lavfi", "-t", seconds, "-i", "anullsrc=channel_layout=stereo:sample_rate=48000", "-map", "0:v", "-map", "1:a"]);
        }
        else
        {
            var chains = new List<string>();
            var limiter = $"alimiter=limit={Number(AudioMix.Ceiling)}:level=false";
            for (var index = 0; index < heard.Count; index++)
            {
                var track = heard[index];
                var gain = AudioMix.Gain(await MeasureAsync(jobDirectory, track.File, seconds, cancellationToken));
                // No more of a track is read than the video has room for. It is said to be a WAV: nothing is guessed from the file.
                arguments.AddRange(["-f", "wav", "-t", seconds, "-i", track.File]);
                // Brought to the target loudness, its peaks held under the ceiling, and only then turned down to its volume.
                var chain = $"[{index + 1}:a]{Conformed},volume={Number(gain)},{limiter},volume={Number(track.VolumePercent / 100.0)}";
                if (track.DurationMs > durationMs)
                {
                    chain += $",afade=t=out:st={Seconds(Math.Max(0, durationMs - AudioMix.FadeOutMs))}:d={Seconds(Math.Min(durationMs, AudioMix.FadeOutMs))}";
                }
                chains.Add($"{chain},apad=whole_dur={seconds}[track{index}]");
            }
            // The tracks are added as they are, and what the sum sends over the ceiling is held under it.
            var all = string.Concat(Enumerable.Range(0, heard.Count).Select(index => $"[track{index}]"));
            chains.Add(heard.Count == 1 ? $"{all}{limiter}[mix]" : $"{all}amix=inputs={heard.Count}:normalize=0,{limiter}[mix]");
            arguments.AddRange(["-filter_complex", string.Join(";", chains), "-map", "0:v", "-map", "[mix]"]);
        }
        arguments.AddRange(["-c:v", "copy", "-c:a", "aac", "-b:a", "128k", "-ar", "48000", "-t", seconds, "-movflags", "+faststart", output]);

        await Processes.RunAsync("ffmpeg", arguments, jobDirectory, jobDirectory, _options.ProcessTimeout, cancellationToken);
    }

    /// <summary>
    /// Lays each Scene's speech where its Scene starts and writes the whole as one WAV as long as the video,
    /// silent wherever nothing is said. It is then a narration track like an uploaded one, and is mixed as one.
    /// </summary>
    /// <param name="pieces">Each a WAV file this system wrote.</param>
    public async Task NarrateAsync(
        string jobDirectory, IReadOnlyList<SpokenPiece> pieces, int durationMs, string output, CancellationToken cancellationToken)
    {
        var seconds = Seconds(durationMs);
        List<string> arguments = ["-hide_banner", "-loglevel", "error", "-y"];
        var chains = new List<string>();
        for (var index = 0; index < pieces.Count; index++)
        {
            arguments.AddRange(["-f", "wav", "-i", pieces[index].File]);
            // Sped up without its pitch changing, when that is what it takes to end with its Scene.
            var tempo = pieces[index].Tempo > 1 ? $",atempo={Number(pieces[index].Tempo)}" : "";
            chains.Add($"[{index}:a]{Conformed}{tempo},adelay={pieces[index].StartMs}:all=1[piece{index}]");
        }
        var all = string.Concat(Enumerable.Range(0, pieces.Count).Select(index => $"[piece{index}]"));
        // The pieces are added as they are: nothing is turned down for being one of several.
        var mixed = pieces.Count == 1 ? all : $"{all}amix=inputs={pieces.Count}:normalize=0,";
        chains.Add($"{mixed}apad=whole_dur={seconds}[narration]");
        arguments.AddRange(["-filter_complex", string.Join(";", chains), "-map", "[narration]", "-t", seconds, "-c:a", "pcm_s16le", "-f", "wav", output]);

        await Processes.RunAsync("ffmpeg", arguments, jobDirectory, jobDirectory, _options.ProcessTimeout, cancellationToken);
    }

    /// <summary>
    /// Copies a video a provider made without whatever sound it has, and without encoding the picture again.
    /// Both files are ones the worker named itself.
    /// </summary>
    public Task WithoutSoundAsync(string directory, string input, string output, CancellationToken cancellationToken) =>
        Processes.RunAsync(
            "ffmpeg",
            ["-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0:v:0", "-c:v", "copy", "-an", "-movflags", "+faststart", output],
            directory, directory, _options.ProcessTimeout, cancellationToken);

    // How loud the part of a track that the video has room for is, in LUFS, as it will be mixed: measured, and nothing written.
    private async Task<double> MeasureAsync(string jobDirectory, string file, string seconds, CancellationToken cancellationToken)
    {
        var (_, said) = await Processes.RunForBothAsync(
            "ffmpeg",
            [
                "-hide_banner", "-nostats", "-loglevel", "info", "-f", "wav", "-t", seconds, "-i", file,
                "-af", $"{Conformed},loudnorm=print_format=json", "-f", "null", "-",
            ],
            jobDirectory, jobDirectory, _options.ProcessTimeout, cancellationToken);

        // The filter reports what it measured last of all, as JSON whose numbers are text.
        int from = said.LastIndexOf('{'), to = said.LastIndexOf('}');
        if (from < 0 || to < from) throw new InvalidOperationException($"FFmpeg did not say how loud {Path.GetFileName(file)} is: {said}");
        using var measured = JsonDocument.Parse(said[from..(to + 1)]);
        // Silence is reported as "-inf".
        return double.TryParse(Text(measured.RootElement, "input_i"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lufs)
            && double.IsFinite(lufs)
            ? lufs
            : double.NegativeInfinity;
    }

    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Seconds(int milliseconds) => (milliseconds / 1000m).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Everything about the file that is not what a Rendered Video must be: 1080 by
    /// 1920, H.264 video tagged BT.709, an AAC audio track, an MP4, and the duration
    /// asked for. Empty when it is all of them.
    /// </summary>
    public async Task<IReadOnlyList<string>> ProblemsAsync(string file, int durationMs, CancellationToken cancellationToken)
    {
        var probed = await Processes.RunAsync(
            "ffprobe", ["-v", "error", "-show_streams", "-show_format", "-of", "json", file],
            Path.GetDirectoryName(file)!, Path.GetDirectoryName(file)!, _options.ProcessTimeout, cancellationToken);
        using var document = JsonDocument.Parse(probed);
        var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var format = document.RootElement.GetProperty("format");
        var video = streams.FirstOrDefault(stream => Text(stream, "codec_type") == "video");
        var audio = streams.FirstOrDefault(stream => Text(stream, "codec_type") == "audio");

        var problems = new List<string>();
        if (video.ValueKind != JsonValueKind.Object)
        {
            problems.Add("there is no video");
        }
        else
        {
            var size = (video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32());
            if (size != (RenderedVideo.Width, RenderedVideo.Height)) problems.Add($"the picture is {size.Item1} by {size.Item2}");
            if (Text(video, "codec_name") != "h264") problems.Add($"the video is {Text(video, "codec_name")}");
            string?[] colour = [Text(video, "color_space"), Text(video, "color_primaries"), Text(video, "color_transfer")];
            if (colour.Any(tag => tag != "bt709")) problems.Add($"the colour is tagged {string.Join("/", colour.Select(tag => tag ?? "nothing"))}");
        }
        if (audio.ValueKind != JsonValueKind.Object) problems.Add("there is no audio track");
        else if (Text(audio, "codec_name") != "aac") problems.Add($"the audio is {Text(audio, "codec_name")}");
        if (Text(format, "format_name")?.Split(',').Contains("mp4") != true) problems.Add($"the file is {Text(format, "format_name")}");

        var seconds = double.Parse(Text(format, "duration") ?? "0", CultureInfo.InvariantCulture);
        if (Math.Abs(seconds - durationMs / 1000.0) > DurationToleranceSeconds)
        {
            problems.Add($"it lasts {seconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds");
        }
        return problems;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value.GetString() : null;
}

/// <summary>One of the tracks mixed into a video: the narration or the music.</summary>
/// <param name="File">A WAV file this system wrote, in the job's folder.</param>
/// <param name="DurationMs">How long the whole of it lasts.</param>
/// <param name="VolumePercent">How loud it is in the mix, from 0 to 100, once it is as loud as any other track.</param>
internal sealed record AudioTrack(string File, int DurationMs, int VolumePercent);

/// <summary>One Scene's speech, to be laid into the narration.</summary>
/// <param name="File">A WAV file this system wrote, in the job's folder.</param>
/// <param name="StartMs">Where its Scene starts in the video.</param>
/// <param name="Tempo">How much faster than it was made it is played, so that it ends with its Scene: 1 for as it is.</param>
internal sealed record SpokenPiece(string File, int StartMs, double Tempo);
