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

    private readonly RenderingOptions _options = options.Value;

    /// <summary>
    /// Joins the clips in order, without encoding the video a second time, under a
    /// silent audio track: a video with no narration still has one, so it plays everywhere.
    /// </summary>
    public async Task JoinAsync(string jobDirectory, IReadOnlyList<string> clips, int durationMs, string output, CancellationToken cancellationToken)
    {
        var listing = Path.Combine(jobDirectory, "clips.txt");
        await File.WriteAllLinesAsync(listing, clips.Select(clip => $"file '{clip.Replace("'", @"'\''")}'"), cancellationToken);
        var seconds = (durationMs / 1000m).ToString(CultureInfo.InvariantCulture);

        await Processes.RunAsync(
            "ffmpeg",
            [
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "concat", "-safe", "0", "-i", listing,
                "-f", "lavfi", "-t", seconds, "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
                "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "128k",
                "-t", seconds, "-movflags", "+faststart", output,
            ],
            jobDirectory, jobDirectory, _options.ProcessTimeout, cancellationToken);
    }

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
