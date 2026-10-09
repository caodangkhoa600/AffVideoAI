using AffiVideo.Application.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Renders the queued jobs, oldest first, as many at once as it is configured to.
/// Each job has a scope of its own, which acts for that job's Organization and no
/// other, and is kept by renewing its lease for as long as the work goes on.
/// </summary>
internal sealed class RenderWorker(
    IServiceScopeFactory scopes,
    IOptions<RenderingOptions> options,
    IOptions<RenderQueueOptions> queueOptions,
    ILogger<RenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Whatever a worker that was killed left behind. This is the only worker on the machine.
        var workDirectory = options.Value.WorkDirectory;
        if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);

        var jobsAtOnce = Math.Max(1, options.Value.JobsAtOnce);
        logger.LogInformation("Rendering up to {JobsAtOnce} jobs at once", jobsAtOnce);
        await Task.WhenAll(Enumerable.Range(0, jobsAtOnce).Select(_ => ServeAsync(stoppingToken)));
    }

    // One of the worker's places: it takes a job, renders it, and takes the next.
    private async Task ServeAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var rendered = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var work = await scope.ServiceProvider.GetRequiredService<IRenderQueue>().ClaimAsync(stoppingToken);
                if (work is not null)
                {
                    rendered = true;
                    logger.LogInformation(
                        "Rendering job {JobId} for Storyboard {StoryboardId}, attempt {Attempt}", work.Job.Id, work.Storyboard.Id, work.Job.Attempt);
                    await RenderWhileHeldAsync(scope.ServiceProvider.GetRequiredService<RenderJobRunner>(), work, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The database is away, or a job could not even be marked as failed. The worker carries on.
                logger.LogError(exception, "The render queue could not be served");
                rendered = false;
            }

            if (!rendered) await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // The work goes on only while the job is this worker's: when the lease cannot be
    // renewed, because a member cancelled the job or it went back to the queue, the
    // work is stopped where it is and the programs it started are stopped with it.
    private async Task RenderWhileHeldAsync(RenderJobRunner runner, RenderWork work, CancellationToken stoppingToken)
    {
        using var held = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewing = KeepLeaseAsync(work, held);
        try
        {
            await runner.RunAsync(work, held.Token);
        }
        finally
        {
            await held.CancelAsync();
            await renewing;
        }
    }

    private async Task KeepLeaseAsync(RenderWork work, CancellationTokenSource held)
    {
        using var timer = new PeriodicTimer(queueOptions.Value.LeaseRenewalInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(held.Token)) return;

                // A scope of its own: the job's scope is busy with the job.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IRenderQueue>().RenewAsync(work, held.Token)) continue;

                logger.LogInformation("Render job {JobId} is no longer this worker's; stopping work on it", work.Job.Id);
                await held.CancelAsync();
                return;
            }
            catch (OperationCanceledException) when (held.IsCancellationRequested)
            {
                // The work has ended, one way or another.
                return;
            }
            catch (Exception exception)
            {
                // The database is away. The lease is tried again at the next tick; if it runs out first, the job is another worker's.
                logger.LogWarning(exception, "The lease of render job {JobId} could not be renewed", work.Job.Id);
            }
        }
    }
}
