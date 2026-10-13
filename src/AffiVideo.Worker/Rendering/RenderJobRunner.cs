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
        var problems = StoryboardRules.RenderProblems(
            storyboard, work.Assets, work.Stills.Select(still => still.Id).ToHashSet(), work.Clips.Select(clip => clip.Id).ToHashSet());
        if (problems.Count > 0) throw new RenderFailedException(string.Join(" ", problems));
        // Speech is for the whole video or for none of it: once a Scene of the version has some, a Scene left
        // unspoken would be a silence nobody chose. With no speech at all the video is silent, as it always was.
        if (work.Speech.Count > 0)
        {
            var made = work.Speech.Where(spoken => spoken.Speech.State == SpeechState.Made).Select(spoken => spoken.ScenePosition).ToHashSet();
            var unspoken = storyboard.Scenes
                .Where(scene => !string.IsNullOrWhiteSpace(scene.NarrationText) && !made.Contains(scene.Position))
                .Select(scene => $"The narration text of Scene {scene.Position} has not been spoken.")
                .ToList();
            if (unspoken.Count > 0)
            {
                throw new RenderFailedException(
                    $"{string.Join(" ", unspoken)} Other Scenes of this version have their speech. Confirm the estimate at Storyboard review " +
                    "and wait for the speech to be made, or upload a narration, then render again.");
            }
            var tooLong = work.Speech
                .Select(spoken => (spoken.ScenePosition, spoken.Speech.DurationMs, SceneMs: storyboard.Scenes.Single(scene => scene.Position == spoken.ScenePosition).DurationMs))
                .Where(fit => SpeechFit.TempoFor(fit.DurationMs, fit.SceneMs) is null)
                .Select(fit =>
                    $"The speech of Scene {fit.ScenePosition} lasts {fit.DurationMs / 1000m:0.#} seconds and the Scene {fit.SceneMs / 1000m:0.#}: " +
                    "speeding it up by a tenth, which is the most that is done, does not make it fit.")
                .ToList();
            if (tooLong.Count > 0)
            {
                throw new RenderFailedException(
                    $"{string.Join(" ", tooLong)} Shorten the narration text or give the Scene more time, generate the speech again, then render.");
            }
        }

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

        // A generated backdrop is its approved Still, as it was made: the render only fetches it (ADR 0003).
        var stillOf = new Dictionary<Guid, GeneratedInput>();
        foreach (var still in work.Stills)
        {
            await using var kept = await storage.OpenAsync(still.StorageKey, cancellationToken)
                ?? throw new RenderFailedException("A Still the Storyboard uses is no longer stored. Ask for another, approve it, then render the version that makes.");
            using var read = new MemoryStream();
            await kept.CopyToAsync(read, cancellationToken);
            var png = read.ToArray();
            var file = $"still-{still.Id:N}.png";
            await File.WriteAllBytesAsync(Path.Combine(images, file), png, cancellationToken);
            stillOf[still.Id] = new GeneratedInput($"{Remotion.ImagesFolder}/{file}", png);
        }
        var stillBehind = scenes
            .Select(scene => scene is { Technique: Technique.GeneratedBackdrop, StillId: { } id } ? stillOf[id] : null)
            .ToList();

        // Generated footage is its approved Clip, as it was made: fetched like a Still, and no provider is asked for anything.
        var footageOf = new Dictionary<Guid, GeneratedInput>();
        foreach (var clip in work.Clips)
        {
            await using var kept = await storage.OpenAsync(clip.StorageKey, cancellationToken)
                ?? throw new RenderFailedException("A Clip the Storyboard uses is no longer stored. Describe the Scene's footage again, generate it, then render the version that makes.");
            using var read = new MemoryStream();
            await kept.CopyToAsync(read, cancellationToken);
            var content = read.ToArray();
            var file = $"clip-{clip.Id:N}.mp4";
            await File.WriteAllBytesAsync(Path.Combine(images, file), content, cancellationToken);
            footageOf[clip.Id] = new GeneratedInput($"{Remotion.ImagesFolder}/{file}", content);
        }
        var footage = scenes
            .Select(scene => scene is { Technique: Technique.GeneratedFootage, ClipId: { } id } ? footageOf[id] : null)
            .ToList();
        // A Scene of generated footage has no Product in it. It carries nothing on from the Scene before, and
        // leaves nothing on screen for the Scene after, which starts as the video's first Scene does.
        var carriesOn = scenes.Select((_, index) => index > 0 && footage[index] is null && footage[index - 1] is null).ToList();

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
                carriesOn[index] ? new PreviousSceneInput(scenes[index - 1].Layout, layerOf[photos[index - 1].Id].Input) : null,
                $"{Remotion.ImagesFolder}/{BackdropFile}",
                stillBehind[index]?.File,
                footage[index]?.File,
                colours)))
            .ToList();
        // Everything that decides a Scene's frames: what its template is handed, and the images that names.
        var keys = scenes
            .Select((_, index) => SceneClips.KeyOf(
                remotion.Look, inputs[index],
                [
                    backdrop, layerOf[photos[index].Id].Png,
                    .. carriesOn[index] ? [layerOf[photos[index - 1].Id].Png] : Array.Empty<byte[]>(),
                    // Last, and only for the Scene that shows it: with another Still or Clip that Scene is drawn again, and no other is.
                    .. stillBehind[index] is { } still ? [still.Content] : Array.Empty<byte[]>(),
                    .. footage[index] is { } filling ? [filling.Content] : Array.Empty<byte[]>(),
                ]))
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
        // Speech made from narration text is the narration: each Scene's piece laid where its Scene starts, as one track.
        if (work.Speech.Count > 0)
        {
            var pieces = new List<SpokenPiece>();
            foreach (var spoken in work.Speech)
            {
                var file = Path.Combine(jobDirectory, $"speech-{spoken.ScenePosition}.wav");
                await using (var kept = await storage.OpenAsync(spoken.Speech.StorageKey, cancellationToken))
                {
                    if (kept is null)
                    {
                        throw new RenderFailedException(
                            $"The speech of Scene {spoken.ScenePosition} is no longer stored. Generate again, then render.");
                    }
                    await using var written = File.Create(file);
                    await kept.CopyToAsync(written, cancellationToken);
                }
                var startMs = scenes.TakeWhile(scene => scene.Position != spoken.ScenePosition).Sum(scene => scene.DurationMs);
                var sceneMs = scenes.Single(scene => scene.Position == spoken.ScenePosition).DurationMs;
                pieces.Add(new SpokenPiece(file, startMs, SpeechFit.TempoFor(spoken.Speech.DurationMs, sceneMs) ?? 1));
            }
            var narration = Path.Combine(jobDirectory, "spoken-narration.wav");
            await ffmpeg.NarrateAsync(jobDirectory, pieces, durationMs, narration, cancellationToken);
            tracks.Insert(0, new AudioTrack(narration, durationMs, VariantAudio.MaxVolumePercent));
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

    /// <summary>A Still or a Clip as a Scene is handed it.</summary>
    /// <param name="File">Within the bundle's public folder.</param>
    private sealed record GeneratedInput(string File, byte[] Content);

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
