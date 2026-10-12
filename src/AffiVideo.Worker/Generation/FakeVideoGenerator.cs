using System.Globalization;
using AffiVideo.Application.Providers;
using AffiVideo.Domain;
using AffiVideo.Worker.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Generation;

// The fake video provider. It calls nobody and reads nothing of the Still or the
// description: every Clip it makes is FFmpeg's test pattern, as long as it was
// asked for, which nobody takes for footage. Like a real provider it puts sound
// in what it returns, a steady tone, for the worker to remove. What it made is
// labelled as fake wherever it is shown. It is here, and not beside the fake
// image provider, because only the worker has FFmpeg.
internal sealed class FakeVideoGenerator(FakeGenerationOptions options, IOptions<RenderingOptions> rendering) : IVideoGenerator
{
    public const string Model = "fake-clip";

    private int _asked;

    public VideoProvider Provider => VideoProvider.Fake;

    public async Task<VideoGeneration> GenerateAsync(byte[] still, string description, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (options.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds), cancellationToken);

        // It misbehaves only as it is configured to, so that what a member is shown then can be seen.
        if (options.Refuses)
        {
            return new VideoGeneration.Refused("The fake video provider refuses every Clip, as it is configured to.", options.BillsFailures);
        }
        if (Interlocked.Increment(ref _asked) <= options.TechnicalFailures)
        {
            return new VideoGeneration.Failed("The fake video provider failed, as it is configured to.", options.BillsFailures);
        }

        var directory = Path.Combine(rendering.Value.WorkDirectory, $"fake-clip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "clip.mp4");
            var size = $"{RenderedVideo.Width}x{RenderedVideo.Height}";
            await Processes.RunAsync(
                "ffmpeg",
                [
                    "-hide_banner", "-loglevel", "error", "-y",
                    "-f", "lavfi", "-i", $"testsrc2=size={size}:rate={RenderedVideo.FramesPerSecond}",
                    "-f", "lavfi", "-i", "sine=frequency=330:sample_rate=48000",
                    "-t", duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-movflags", "+faststart", file,
                ],
                directory, directory, rendering.Value.ProcessTimeout, cancellationToken);
            return new VideoGeneration.Made(await File.ReadAllBytesAsync(file, cancellationToken), Model);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

// What the worker has when no video provider is configured. A Clip that reaches it was
// confirmed by an API that has one: the two are configured differently, and the Clip fails, charged for by nobody.
internal sealed class NoVideoGenerator : IVideoGenerator
{
    public VideoProvider Provider => VideoProvider.None;

    public Task<VideoGeneration> GenerateAsync(byte[] still, string description, TimeSpan duration, CancellationToken cancellationToken) =>
        Task.FromResult<VideoGeneration>(new VideoGeneration.Failed("The worker has no video provider configured.", Billed: false));
}
