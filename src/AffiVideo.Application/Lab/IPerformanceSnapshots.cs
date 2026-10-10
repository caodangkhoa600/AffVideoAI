using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>
/// The Performance Snapshots of the Published Posts of the caller's Organization.
/// A Published Post of another Organization is answered exactly as one that does
/// not exist. Nothing here changes or removes a snapshot.
/// </summary>
public interface IPerformanceSnapshots
{
    /// <summary>Records totals a member typed in.</summary>
    /// <returns>Null when there is no such Published Post.</returns>
    Task<PerformanceSnapshotOutcome?> RecordManualAsync(
        Guid postId, DateTimeOffset takenAt, PerformanceTotals totals, CancellationToken cancellationToken);

    /// <summary>The latest first: by the moment the totals apply to, then by when they were entered.</summary>
    /// <returns>Null when there is no such Published Post.</returns>
    Task<Page<PerformanceSnapshotRecord>?> ListAsync(Guid postId, PageRequest page, CancellationToken cancellationToken);
}

/// <param name="LowerThanPrevious">The totals that are lower than in the snapshot before this one.</param>
public sealed record PerformanceSnapshotRecord(PerformanceSnapshot Snapshot, IReadOnlyList<PerformanceMetric> LowerThanPrevious);

/// <summary>The snapshot as recorded; or what in the request was refused, by field, in words for the member.</summary>
public sealed record PerformanceSnapshotOutcome(PerformanceSnapshotRecord? Record, IReadOnlyDictionary<string, string>? Invalid)
{
    public static PerformanceSnapshotOutcome Recorded(PerformanceSnapshotRecord record) => new(record, null);

    public static PerformanceSnapshotOutcome Refuse(string field, string reason) =>
        new(null, new Dictionary<string, string> { [field] = reason });
}
