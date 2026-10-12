using AffiVideo.Application.Generation;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Domain;
using AffiVideo.Worker.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Generation;

/// <summary>
/// Has the image provider make the queued Stills, oldest first, one at a time.
/// Each Still has a scope of its own, which acts for that Still's Organization and
/// no other, and is kept by renewing its lease for as long as the provider takes.
/// Nothing here decides to spend: every Still in the queue was confirmed by a member.
/// </summary>
internal sealed class StillWorker(
    IServiceScopeFactory scopes,
    IImageGenerator images,
    IOptions<RenderingOptions> options,
    IOptions<RenderQueueOptions> queueOptions,
    ILogger<StillWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Making Stills with the {Provider} image provider", images.Provider);
        while (!stoppingToken.IsCancellationRequested)
        {
            var made = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<IStillQueue>();
                if (await queue.ClaimAsync(stoppingToken) is { } work)
                {
                    made = true;
                    logger.LogInformation("Making Still {StillId}, attempt {Attempt}", work.Still.Id, work.Still.Attempt);
                    await MakeWhileHeldAsync(queue, work, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The database is away, or a Still could not even be marked as failed. The worker carries on.
                logger.LogError(exception, "The queue of Stills could not be served");
                made = false;
            }

            if (!made) await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // The provider is waited for only while the Still is this worker's.
    private async Task MakeWhileHeldAsync(IStillQueue queue, StillWork work, CancellationToken stoppingToken)
    {
        using var held = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewing = KeepLeaseAsync(work, held);
        try
        {
            switch (await images.GenerateAsync(work.Still.Description, held.Token))
            {
                case ImageGeneration.Made made:
                    await KeepAsync(queue, work, made, stoppingToken);
                    break;
                case ImageGeneration.Refused refused:
                    await queue.FailAsync(
                        work, StillFailureKind.Refused,
                        $"The image provider would not make this picture: {refused.Reason} Change the Scene's description, then generate again.",
                        refused.Reason, refused.Billed, stoppingToken);
                    break;
                case ImageGeneration.Failed failed:
                    await queue.FailAsync(
                        work, StillFailureKind.Technical,
                        "The picture could not be made: something went wrong at the image provider. Ask for it again.",
                        failed.Detail, failed.Billed, stoppingToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (held.IsCancellationRequested)
        {
            // The worker is stopping, or the lease was lost: the Still goes back to the queue when its lease runs out.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Still {StillId} could not be made", work.Still.Id);
            await queue.FailAsync(
                work, StillFailureKind.Technical,
                "The picture could not be made: something went wrong in the worker. Ask for it again.",
                exception.ToString(), billed: false, stoppingToken);
        }
        finally
        {
            await held.CancelAsync();
            await renewing;
        }
    }

    // The provider has made the picture, and so has charged for it. If it cannot be kept, the Still
    // fails as paid for: asking the provider again would be paying again without being asked.
    private async Task KeepAsync(IStillQueue queue, StillWork work, ImageGeneration.Made made, CancellationToken stoppingToken)
    {
        try
        {
            using var png = new MemoryStream(made.Png);
            if (!await queue.CompleteAsync(work, made.Model, png, stoppingToken))
            {
                logger.LogInformation("Still {StillId} was no longer this worker's when it was made; the picture was not kept", work.Still.Id);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The picture of Still {StillId} was made and could not be kept", work.Still.Id);
            await queue.FailAsync(
                work, StillFailureKind.Technical,
                "The picture was made and could not be kept: something went wrong in the worker. It is counted as paid for and was " +
                "not asked for again. Ask for it again if you still want it.",
                exception.ToString(), billed: true, stoppingToken);
        }
    }

    private async Task KeepLeaseAsync(StillWork work, CancellationTokenSource held)
    {
        using var timer = new PeriodicTimer(queueOptions.Value.LeaseRenewalInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(held.Token)) return;

                // A scope of its own: the Still's scope is busy with the Still.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IStillQueue>().RenewAsync(work, held.Token)) continue;

                logger.LogInformation("Still {StillId} is no longer this worker's; stopping work on it", work.Still.Id);
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
                logger.LogWarning(exception, "The lease of Still {StillId} could not be renewed", work.Still.Id);
            }
        }
    }
}
