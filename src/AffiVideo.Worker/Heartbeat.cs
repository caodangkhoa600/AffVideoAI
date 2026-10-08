using AffiVideo.Application.SystemStatus;

namespace AffiVideo.Worker;

/// <summary>
/// Touches a file for as long as the worker can reach the database and object
/// storage. The container's health check reads the file's age, so a worker that
/// has hung or lost either service turns unhealthy.
/// </summary>
internal sealed class Heartbeat(IServiceScopeFactory scopes, ILogger<Heartbeat> logger) : BackgroundService
{
    // compose.yaml's health check for the worker looks for this file.
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "affivideo-worker.heartbeat");

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started; heartbeat file is {File}", FilePath);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<ISystemStatusReader>().ReadAsync(stoppingToken);
            if (status.AllReachable)
            {
                await File.WriteAllTextAsync(FilePath, DateTimeOffset.UtcNow.ToString("O"), stoppingToken);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
