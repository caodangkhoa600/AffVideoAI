using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Lab;

// No method names an Organization: the context's filter leaves only the caller's
// Published Posts and their snapshots. A snapshot is only ever added.
internal sealed class ScopedPerformanceSnapshots(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IPerformanceSnapshots
{
    // A member's clock may run a little ahead of this one.
    private static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(5);

    public async Task<PerformanceSnapshotOutcome?> RecordManualAsync(
        Guid postId, DateTimeOffset takenAt, PerformanceTotals totals, CancellationToken cancellationToken)
    {
        if (!await database.PublishedPosts.AnyAsync(p => p.Id == postId, cancellationToken)) return null;

        var now = clock.GetUtcNow();
        if (takenAt > now + ClockAllowance)
        {
            return PerformanceSnapshotOutcome.Refuse("takenAt", "Totals cannot have been read in the future.");
        }

        var snapshot = new PerformanceSnapshot(
            Guid.CreateVersion7(), caller.Organization("A Performance Snapshot"), postId, takenAt, totals, PerformanceSource.Manual, now);
        var previous = await PreviousAsync(postId, snapshot.TakenAt, cancellationToken);

        database.PerformanceSnapshots.Add(snapshot);
        await database.SaveChangesAsync(cancellationToken);
        // As it is kept, which is to the microsecond: the moments it is answered with are the ones it is read back with.
        var recorded = await database.PerformanceSnapshots.AsNoTracking().SingleAsync(s => s.Id == snapshot.Id, cancellationToken);
        return PerformanceSnapshotOutcome.Recorded(new PerformanceSnapshotRecord(recorded, recorded.LowerThan(previous)));
    }

    public async Task<Page<PerformanceSnapshotRecord>?> ListAsync(Guid postId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await database.PublishedPosts.AnyAsync(p => p.Id == postId, cancellationToken)) return null;

        var all = database.PerformanceSnapshots.AsNoTracking().Where(s => s.PublishedPostId == postId);
        var found = await all.LatestFirst().Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        // The snapshots about the earliest moment on the page have the one before them on a later page, if anywhere.
        var beforeThePage = found.Count == 0 ? null : await PreviousAsync(postId, found[^1].TakenAt, cancellationToken);
        var items = found
            .Select((snapshot, index) => new PerformanceSnapshotRecord(
                snapshot,
                snapshot.LowerThan(found.Skip(index + 1).FirstOrDefault(earlier => earlier.TakenAt < snapshot.TakenAt) ?? beforeThePage)))
            .ToList();
        return new Page<PerformanceSnapshotRecord>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    // The snapshot before one about this moment: the latest about an earlier moment. One about the
    // same moment is not before it but beside it: a correction is not compared with what it corrects.
    private Task<PerformanceSnapshot?> PreviousAsync(Guid postId, DateTimeOffset takenAt, CancellationToken cancellationToken) =>
        database.PerformanceSnapshots.AsNoTracking()
            .Where(s => s.PublishedPostId == postId && s.TakenAt < takenAt)
            .LatestFirst()
            .FirstOrDefaultAsync(cancellationToken);
}

internal static class PerformanceSnapshotQueries
{
    /// <summary>
    /// The order in which the first is the current figure: by the moment the totals apply
    /// to, and for the same moment by when they were entered, which is how a correction wins.
    /// </summary>
    public static IOrderedQueryable<PerformanceSnapshot> LatestFirst(this IQueryable<PerformanceSnapshot> snapshots) =>
        snapshots.OrderByDescending(s => s.TakenAt).ThenByDescending(s => s.RecordedAt).ThenByDescending(s => s.Id);
}
