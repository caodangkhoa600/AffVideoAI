using AffiVideo.Application.Rendering;
using AffiVideo.Domain;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Takes one claimed job through its stages to a Rendered Video, or to a failure
/// with a reason. Whatever happens, the job's temporary files are gone when it returns.
/// </summary>
internal sealed class RenderJobRunner(
    IRenderQueue queue,
    VideoLayers layers,
    Remotion remotion,
    Ffmpeg ffmpeg,
    IOptions<RenderingOptions> options,
    ILogger<RenderJobRunner> logger)
{
    private const string BackdropFile = "backdrop.png";

    private readonly RenderingOptions _options = options.Value;

    /// <param name="work">A job that has just been claimed, and so is validating.</param>
    public async Task RunAsync(RenderWork work, CancellationToken cancellationToken)
    {
        var jobDirectory = Path.Combine(_options.WorkDirectory, work.Job.Id.ToString("N"));
        try
        {
            await RenderAsync(work, jobDirectory, cancellationToken);
            logger.LogInformation("Render job {JobId} completed", work.Job.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The worker is stopping. The job is left as it is, not failed.
            throw;
        }
        catch (RenderFailedException refused)
        {
            logger.LogWarning("Render job {JobId} failed while {State}: {Reason}", work.Job.Id, work.Job.State, refused.Message);
            await queue.FailAsync(work, refused.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            // The member is told which stage it was; what went wrong inside it is for the log.
            logger.LogError(exception, "Render job {JobId} failed while {State}", work.Job.Id, work.Job.State);
            await queue.FailAsync(
                work, $"The video could not be rendered: something went wrong while {Doing(work.Job.State)}. Try rendering again.", cancellationToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(jobDirectory)) Directory.Delete(jobDirectory, recursive: true);
            }
            catch (IOException exception)
            {
                // Not a reason to lose what the job itself had to say. The worker clears the folder when it next starts.
                logger.LogWarning(exception, "The temporary files of render job {JobId} could not be deleted", work.Job.Id);
            }
        }
    }

    private async Task RenderAsync(RenderWork work, string jobDirectory, CancellationToken cancellationToken)
    {
        var storyboard = work.Storyboard;
        var problems = StoryboardRules.RenderProblems(storyboard, work.Assets);
        if (problems.Count > 0) throw new RenderFailedException(string.Join(" ", problems));

        // What each Scene is drawn from: its photo, and the Scene before it, whose Product it carries on from.
        await queue.MoveAsync(work, RenderJobState.Planning, cancellationToken);
        var scenes = storyboard.Scenes;
        var photos = scenes.Select(scene => work.Assets.Single(asset => asset.Id == scene.AssetIds[0])).ToList();

        await queue.MoveAsync(work, RenderJobState.GeneratingAssets, cancellationToken);
        Directory.CreateDirectory(jobDirectory);
        var images = remotion.Prepare(jobDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(images, BackdropFile), Backdrop.Studio(RenderedVideo.Width, RenderedVideo.Height).EncodePng(), cancellationToken);
        var layerOf = new Dictionary<Guid, (LayerInput Input, VideoLayerDetails Details)>();
        foreach (var photo in photos.DistinctBy(photo => photo.Id))
        {
            var layer = await layers.ForAsync(photo, cancellationToken);
            var file = $"product-{photo.Id:N}.png";
            await File.WriteAllBytesAsync(Path.Combine(images, file), layer.Png, cancellationToken);
            var details = layer.Details;
            layerOf[photo.Id] = (
                new LayerInput($"{Remotion.ImagesFolder}/{file}", details.Width, details.Height, details.ProductWidth, details.ProductHeight),
                details);
        }

        await queue.MoveAsync(work, RenderJobState.Rendering, cancellationToken);
        // One palette for the whole video, from the photo it opens on.
        var first = layerOf[photos[0].Id].Details;
        var colours = Palette.From(first.Hue, first.Saturation);
        var inputs = scenes
            .Select((scene, index) => new SceneInput(
                storyboard.CreativeTemplate,
                scene.Layout,
                scene.DurationMs * RenderedVideo.FramesPerSecond / 1000,
                scene.OnScreenText,
                layerOf[photos[index].Id].Input,
                index == 0 ? null : new PreviousSceneInput(scenes[index - 1].Layout, layerOf[photos[index - 1].Id].Input),
                $"{Remotion.ImagesFolder}/{BackdropFile}",
                colours))
            .ToList();
        var clips = scenes.Select(scene => Path.Combine(jobDirectory, $"scene-{scene.Position}.mp4")).ToList();
        // A Scene needs nothing of its neighbours' frames, so several are drawn at once.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, scenes.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.ScenesAtOnce), CancellationToken = cancellationToken },
            async (index, cancellation) =>
                await remotion.RenderAsync(jobDirectory, scenes[index].Position, inputs[index], clips[index], cancellation));

        var durationMs = scenes.Sum(scene => scene.DurationMs);
        var video = Path.Combine(jobDirectory, "video.mp4");
        await ffmpeg.JoinAsync(jobDirectory, clips, durationMs, video, cancellationToken);

        await queue.MoveAsync(work, RenderJobState.QualityReview, cancellationToken);
        var wrong = await ffmpeg.ProblemsAsync(video, durationMs, cancellationToken);
        if (wrong.Count > 0)
        {
            throw new InvalidOperationException($"The rendered file is not what a Rendered Video must be: {string.Join("; ", wrong)}.");
        }

        var uncut = photos.Where(photo => !layerOf[photo.Id].Details.CutOut).Select(photo => photo.Id).Distinct().ToList();
        await using var mp4 = File.OpenRead(video);
        await queue.CompleteAsync(work, mp4, uncut, cancellationToken);
    }

    private static string Doing(RenderJobState state) => state switch
    {
        RenderJobState.Validating => "checking the Storyboard",
        RenderJobState.Planning => "planning the Scenes",
        RenderJobState.GeneratingAssets => "preparing the Product's photos",
        RenderJobState.Rendering => "drawing the Scenes",
        RenderJobState.QualityReview => "checking the finished video",
        // The job is completed in memory before its video is stored, and it is the storing that failed.
        RenderJobState.Completed => "storing the finished video",
        _ => "rendering",
    };
}
