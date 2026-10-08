namespace AffiVideo.Application.SystemStatus;

/// <summary>Reports whether the services the application depends on can be reached right now.</summary>
public interface ISystemStatusReader
{
    /// <summary>Does not throw for an unreachable dependency; that is what it reports.</summary>
    Task<SystemStatus> ReadAsync(CancellationToken cancellationToken);
}

public sealed record SystemStatus(bool DatabaseReachable, bool ObjectStorageReachable)
{
    public bool AllReachable => DatabaseReachable && ObjectStorageReachable;
}
