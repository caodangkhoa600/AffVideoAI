using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

// Part of the Affiliate Lab: all of it is mapped on the group MapLab returns.
internal static class PerformanceSnapshotEndpoints
{
    // The Performance Snapshots of a Published Post of the caller's Organization. A Published
    // Post of another Organization is answered exactly like one that does not exist: 404.
    // There is nothing to change or remove a snapshot with: a wrong figure is corrected by a newer one.
    public static void MapPerformanceSnapshots(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/published-posts/{postId:guid}/performance-snapshots").WithTags("Affiliate Lab");

        group.MapGet("", async Task<Results<Ok<PagedResponse<PerformanceSnapshotResponse>>, NotFound>> (
                Guid postId, int? page, int? pageSize, IPerformanceSnapshots snapshots, CancellationToken cancellationToken) =>
                await snapshots.ListAsync(postId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListPerformanceSnapshots")
            .WithSummary(
                "The Performance Snapshots of a Published Post, the latest first: by the moment the totals apply to, " +
                "then by when they were entered. The first is the current figure.");

        group.MapPost("", async Task<Results<Created<PerformanceSnapshotResponse>, ValidationProblem, NotFound>> (
                Guid postId, PerformanceSnapshotRequest request, IPerformanceSnapshots snapshots, CancellationToken cancellationToken) =>
                await snapshots.RecordManualAsync(
                        postId, request.TakenAt,
                        new PerformanceTotals(request.Views, request.Likes, request.Comments, request.Shares, request.Clicks),
                        cancellationToken) switch
                    {
                        null => TypedResults.NotFound(),
                        { Record: { } recorded } => TypedResults.Created((string?)null, ToResponse(recorded)),
                        var refused => TypedResults.ValidationProblem(
                            refused.Invalid!.ToDictionary(field => field.Key, field => new[] { field.Value })),
                    })
            .WithName("RecordPerformanceSnapshot")
            .WithSummary(
                "Records the running totals a member read off the platform for a Published Post, as manual entry. " +
                "A total left out is unknown, not zero. Totals lower than in the snapshot before are accepted, and " +
                "the answer names them.");
    }

    public static CurrentPerformanceResponse ToCurrent(PerformanceSnapshot snapshot) => new(
        snapshot.Id, snapshot.TakenAt, snapshot.Views, snapshot.Likes, snapshot.Comments, snapshot.Shares, snapshot.Clicks,
        snapshot.Source, snapshot.RecordedAt);

    private static PerformanceSnapshotResponse ToResponse(PerformanceSnapshotRecord record) => new(
        record.Snapshot.Id,
        record.Snapshot.PublishedPostId,
        record.Snapshot.TakenAt,
        record.Snapshot.Views,
        record.Snapshot.Likes,
        record.Snapshot.Comments,
        record.Snapshot.Shares,
        record.Snapshot.Clicks,
        record.Snapshot.Source,
        record.Snapshot.RecordedAt,
        record.LowerThanPrevious);
}
