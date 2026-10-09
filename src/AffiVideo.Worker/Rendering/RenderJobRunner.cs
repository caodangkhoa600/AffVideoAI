using System.Collections.Concurrent;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
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
    SceneClips keptClips,
    IObjectStorage storage,
    Ffmpeg ffmpeg,
    IOptions<RenderingOptions> options,
    ILogger<RenderJobRunner> logger)
{
    private const string BackdropFile = "backdrop.png";

    private readonly RenderingOptions _options = options.Value;

    /// <param name="work">A job that has just been claimed, and so is validating.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the worker is stopping or the job is no longer its own. The
    /// work stops there, the programs it started with it, and the job is left as it is.
    /// </param>
    public async Task RunAsync(RenderWork work, CancellationToken cancellationToken)
    {
        // A folder for this attempt alone: an attempt that is still stopping never shares one with the next.
        var jobDirectory = Path.Combine(_options.WorkDirectory, $"{work.Job.Id:N}-{work.Job.Attempt}");
        try
        {
            await RenderAsync(work, jobDirectory, cancellationToken);
            logger.LogInformation("Render job {JobId} completed", work.Job.Id);
        }
        catch (Exception stopped) when (stopped is RenderStoppedException || cancellationToken.IsCancellationRequested)
        {
            // Cancelled by a member, taken back by the queue, or the worker is stopping. Whatever
            // was thrown on the way out, it is not a failure of the job's: nothing is recorded.
            logger.LogInformation("Render job {JobId} was stopped while {State}", work.Job.Id, work.Job.State);
        }
        catch (RenderFailedException refused)
        {
            logger.LogWarning("Render job {JobId} failed while {State}: {Reason}", work.Job.Id, work.Job.State, refused.Message);
            await queue.FailAsync(work, RenderFailureCategory.InvalidInput, refused.Message, detail: null, cancellationToken);
        }
        catch (Exception exception)
        {
            // The member is told which stage it was; what went wrong inside it is kept beside that, for whoever looks into it.
            logger.LogError(exception, "Render job {JobId} failed while {State}, attempt {Attempt}", work.Job.Id, work.Job.State, work.Job.Attempt);
            var (category, what) = exception is TimeoutException
                ? (RenderFailureCategory.Timeout, "it took too long")
                : (RenderFailureCategory.Internal, "something went wrong");
            await queue.FailAsync(
                work, category, $"The video could not be rendered: {what} while {Doing(work.Job.State)}. Try rendering again.",
                exception.ToString(), cancellationToken);
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
        var backdrop = Backdrop.Studio(RenderedVideo.Width, RenderedVideo.Height).EncodePng();
        await File.WriteAllBytesAsync(Path.Combine(images, BackdropFile), backdrop, cancellationToken);
        var layerOf = new Dictionary<Guid, (LayerInput Input, VideoLayerDetails Details, byte[] Png)>();
        foreach (var photo in photos.DistinctBy(photo => photo.Id))
        {
            var layer = await layers.ForAsync(photo, cancellationToken);
            var file = $"product-{photo.Id:N}.png";
            await File.WriteAllBytesAsync(Path.Combine(images, file), layer.Png, cancellationToken);
            var details = layer.Details;
            layerOf[photo.Id] = (
                new LayerInput($"{Remotion.ImagesFolder}/{file}", details.Width, details.Height, details.ProductWidth, details.ProductHeight),
                details, layer.Png);
        }

        await queue.MoveAsync(work, RenderJobState.Rendering, cancellationToken);
        // One palette for the whole video, from the photo it opens on.
        var first = layerOf[photos[0].Id].Details;
        var colours = Palette.From(first.Hue, first.Saturation);
        var inputs = scenes
            .Select((scene, index) => Remotion.Describe(new SceneInput(
                storyboard.CreativeTemplate,
                scene.Layout,
                scene.DurationMs * RenderedVideo.FramesPerSecond / 1000,
                scene.OnScreenText,
                layerOf[photos[index].Id].Input,
                index == 0 ? null : new PreviousSceneInput(scenes[index - 1].Layout, layerOf[photos[index - 1].Id].Input),
                $"{Remotion.ImagesFolder}/{BackdropFile}",
                colours)))
            .ToList();
        // Everything that decides a Scene's frames: what its template is handed, and the images that names.
        var keys = scenes
            .Select((_, index) => SceneClips.KeyOf(
                remotion.Look, inputs[index],
                index == 0
                    ? [backdrop, layerOf[photos[index].Id].Png]
                    : [backdrop, layerOf[photos[index].Id].Png, layerOf[photos[index - 1].Id].Png]))
            .ToList();
        var clips = scenes.Select(scene => Path.Combine(jobDirectory, $"scene-{scene.Position}.mp4")).ToList();
        var organizationId = work.Job.OrganizationId;
        var drawn = new ConcurrentBag<int>();
        // A Scene needs nothing of its neighbours' frames, so several are drawn at once. A Scene
        // that an earlier render drew just as it is now is not drawn again: its clip was kept.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, scenes.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.ScenesAtOnce), CancellationToken = cancellationToken },
            async (index, cancellation) =>
            {
                if (await keptClips.FetchAsync(organizationId, keys[index], clips[index], cancellation)) return;

                await remotion.RenderAsync(jobDirectory, scenes[index].Position, inputs[index], clips[index], cancellation);
                drawn.Add(index);
            });

        var durationMs = scenes.Sum(scene => scene.DurationMs);
        var video = Path.Combine(jobDirectory, "video.mp4");
        var tracks = new List<AudioTrack>();
        foreach (var audio in work.Audio)
        {
            // Named here, by its kind: nothing of what the member's file was called gets this far.
            var file = Path.Combine(jobDirectory, $"{audio.Kind.ToString().ToLowerInvariant()}.wav");
            await using (var kept = await storage.OpenAsync(audio.StorageKey, cancellationToken))
            {
                if (kept is null)
                {
                    throw new RenderFailedException(
                        $"The Variant's {audio.Kind.ToString().ToLowerInvariant()} was removed or replaced while the video was being rendered. Render again.");
                }
                await using var written = File.Create(file);
                await kept.CopyToAsync(written, cancellationToken);
            }
            tracks.Add(new AudioTrack(file, audio.DurationMs, audio.VolumePercent));
        }
        await ffmpeg.JoinAsync(jobDirectory, clips, tracks, durationMs, video, cancellationToken);

        await queue.MoveAsync(work, RenderJobState.QualityReview, cancellationToken);
        var wrong = await ffmpeg.ProblemsAsync(video, durationMs, cancellationToken);
        if (wrong.Count > 0)
        {
            throw new InvalidOperationException($"The rendered file is not what a Rendered Video must be: {string.Join("; ", wrong)}.");
        }

        // Only now are the new clips kept: a clip is never reused unless the video it was drawn for passed its checks.
        foreach (var index in drawn)
        {
            await keptClips.KeepAsync(organizationId, keys[index], clips[index], cancellationToken);
        }

        var uncut = photos.Where(photo => !layerOf[photo.Id].Details.CutOut).Select(photo => photo.Id).Distinct().ToList();
        await using var mp4 = File.OpenRead(video);
        await queue.CompleteAsync(work, mp4, uncut, [.. drawn.Select(index => scenes[index].Position).Order()], cancellationToken);
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
