using AffiVideo.Application.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Renders the queued jobs, one after another, oldest first. Each job has a scope
/// of its own, which acts for that job's Organization and no other.
/// </summary>
internal sealed class RenderWorker(
    IServiceScopeFactory scopes, IOptions<RenderingOptions> options, ILogger<RenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Whatever a worker that was killed left behind. This is the only worker on the machine.
        var workDirectory = options.Value.WorkDirectory;
        if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);

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
                    logger.LogInformation("Rendering job {JobId} for Storyboard {StoryboardId}", work.Job.Id, work.Storyboard.Id);
                    await scope.ServiceProvider.GetRequiredService<RenderJobRunner>().RunAsync(work, stoppingToken);
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
}
