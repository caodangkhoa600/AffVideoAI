using AffiVideo.Application.Generation;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Worker.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Generation;

/// <summary>
/// Has the video provider make the queued Clips, oldest first, one at a time, each
/// from its approved Still and as long as its Scene. It is the twin of
/// <see cref="StillWorker"/>: each Clip has a scope of its own, which acts for that
/// Clip's Organization and no other, and is kept by renewing its lease for as long
/// as the provider takes. Nothing here decides to spend: every Clip in the queue
/// was confirmed by a member, who has since approved the Still it starts from.
/// </summary>
internal sealed class ClipWorker(
    IServiceScopeFactory scopes,
    IVideoGenerator videos,
    IObjectStorage storage,
    Ffmpeg ffmpeg,
    IOptions<RenderingOptions> options,
    IOptions<RenderQueueOptions> queueOptions,
    ILogger<ClipWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Making Clips with the {Provider} video provider", videos.Provider);
        while (!stoppingToken.IsCancellationRequested)
        {
            var made = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IClipQueue>();
                if (await queue.ClaimAsync(stoppingToken) is { } work)
                {
                    made = true;
                    logger.LogInformation("Making Clip {ClipId}, attempt {Attempt}", work.Clip.Id, work.Clip.Attempt);
                    await MakeWhileHeldAsync(queue, work, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The database is away, or a Clip could not even be marked as failed. The worker carries on.
                logger.LogError(exception, "The queue of Clips could not be served");
                made = false;
            }

            if (!made) await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // The provider is waited for only while the Clip is this worker's.
    private async Task MakeWhileHeldAsync(IClipQueue queue, ClipWork work, CancellationToken stoppingToken)
    {
        using var held = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewing = KeepLeaseAsync(work, held);
        // From the moment the provider has made the video it has charged for it, whatever goes wrong afterwards.
        var charged = false;
        try
        {
            // Nothing has been asked of the provider yet, so a Still that cannot be read costs nothing.
            var still = await StillOfAsync(work, held.Token);
            switch (await videos.GenerateAsync(still, work.Clip.Description, TimeSpan.FromMilliseconds(work.Clip.DurationMs), held.Token))
            {
                case VideoGeneration.Made made:
                    charged = true;
                    await KeepAsync(queue, work, made, stoppingToken);
                    break;
                case VideoGeneration.Refused refused:
                    await queue.FailAsync(
                        work, ClipFailureKind.Refused,
                        $"The video provider would not make this Clip: {refused.Reason} Change the Scene's description, then generate again.",
                        refused.Reason, refused.Billed, stoppingToken);
                    break;
                case VideoGeneration.Failed failed:
                    await queue.FailAsync(
                        work, ClipFailureKind.Technical,
                        "The Clip could not be made: something went wrong at the video provider. Ask for it again.",
                        failed.Detail, failed.Billed, stoppingToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (held.IsCancellationRequested)
        {
            // The worker is stopping, or the lease was lost: the Clip is failed, as charged for, when its lease runs out.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Clip {ClipId} could not be made", work.Clip.Id);
            // Only what was not charged for is tried again without the member (ADR 0003).
            await queue.FailAsync(
                work, ClipFailureKind.Technical,
                charged
                    ? "The Clip was made and could not be kept: something went wrong in the worker. It is counted as paid for and was " +
                      "not asked for again. Ask for it again if you still want it."
                    : "The Clip could not be made: something went wrong in the worker. Ask for it again.",
                exception.ToString(), billed: charged, stoppingToken);
        }
        finally
        {
            await held.CancelAsync();
            await renewing;
        }
    }

    private async Task<byte[]> StillOfAsync(ClipWork work, CancellationToken cancellationToken)
    {
        await using var kept = await storage.OpenAsync(work.Still.StorageKey, cancellationToken)
            ?? throw new InvalidOperationException($"Still {work.Still.Id}, which Clip {work.Clip.Id} is made from, is not stored.");
        using var read = new MemoryStream();
        await kept.CopyToAsync(read, cancellationToken);
        return read.ToArray();
    }

    // The provider has made the video, and so has charged for it. If it cannot be kept, the Clip
    // fails as paid for: asking the provider again would be paying again without being asked.
    private async Task KeepAsync(IClipQueue queue, ClipWork work, VideoGeneration.Made made, CancellationToken stoppingToken)
    {
        var directory = Path.Combine(options.Value.WorkDirectory, $"clip-{work.Clip.Id:N}-{work.Clip.Attempt}");
        try
        {
            // Named here: nothing of what the provider called its file gets this far.
            Directory.CreateDirectory(directory);
            var asMade = Path.Combine(directory, "made.mp4");
            var silent = Path.Combine(directory, "clip.mp4");
            await File.WriteAllBytesAsync(asMade, made.Mp4, stoppingToken);
            // A video is narrated and set to Music by the member. Whatever sound a provider put in a Clip is not theirs, and goes.
            await ffmpeg.WithoutSoundAsync(directory, asMade, silent, stoppingToken);
            await using var mp4 = File.OpenRead(silent);
            if (!await queue.CompleteAsync(work, made.Model, mp4, stoppingToken))
            {
                logger.LogInformation("Clip {ClipId} was no longer this worker's when it was made; the video was not kept", work.Clip.Id);
            }
        }
        // A storage or database timeout is a cancellation too, and is no less a failure to keep it: only the worker stopping is let through.
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "The video of Clip {ClipId} was made and could not be kept", work.Clip.Id);
            await queue.FailAsync(
                work, ClipFailureKind.Technical,
                "The Clip was made and could not be kept: something went wrong in the worker. It is counted as paid for and was " +
                "not asked for again. Ask for it again if you still want it.",
                exception.ToString(), billed: true, stoppingToken);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException exception)
            {
                logger.LogWarning(exception, "The temporary files of Clip {ClipId} could not be deleted", work.Clip.Id);
            }
        }
    }

    private async Task KeepLeaseAsync(ClipWork work, CancellationTokenSource held)
    {
        using var timer = new PeriodicTimer(queueOptions.Value.LeaseRenewalInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(held.Token)) return;

                // A scope of its own: the Clip's scope is busy with the Clip.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IClipQueue>().RenewAsync(work, held.Token)) continue;

                logger.LogInformation("Clip {ClipId} is no longer this worker's; stopping work on it", work.Clip.Id);
                await held.CancelAsync();
                return;
            }
            catch (OperationCanceledException) when (held.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The database is away. The lease is tried again at the next tick.
                logger.LogWarning(exception, "The lease of Clip {ClipId} could not be renewed", work.Clip.Id);
            }
        }
    }
}
