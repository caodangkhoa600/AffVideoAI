using AffiVideo.Application.SystemStatus;
using AffiVideo.Infrastructure.Persistence;
using AffiVideo.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AffiVideo.Infrastructure;

internal sealed class SystemStatusReader(
    AffiVideoDbContext database,
    S3ObjectStorage storage,
    ILogger<SystemStatusReader> logger) : ISystemStatusReader
{
    // Long enough for a healthy service on the same machine, short enough that
    // the status page is not left waiting on a dead one.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<SystemStatus> ReadAsync(CancellationToken cancellationToken)
    {
        var databaseProbe = ProbeAsync("Database", QueryDatabaseAsync, cancellationToken);
        var storageProbe = ProbeAsync("Object storage", storage.ProbeAsync, cancellationToken);

        return new SystemStatus(await databaseProbe, await storageProbe);
    }

    // A query, not just an open connection: the pool hands back a connection to
    // a server that has since gone away without noticing.
    private Task QueryDatabaseAsync(CancellationToken cancellationToken) =>
        database.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

    private async Task<bool> ProbeAsync(string name, Func<CancellationToken, Task> probe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            await probe(timeout.Token);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The reason goes to the log only: the status endpoint is open to anyone.
            logger.LogWarning(exception, "{Dependency} is unreachable", name);
            return false;
        }
    }
}
