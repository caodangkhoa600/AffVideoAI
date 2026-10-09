using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AffiVideo.Infrastructure.Rendering;

// The queue is the RenderJobs table. Claiming is the one query here that looks
// across Organizations; it identifies the caller as the job's Organization, and
// everything after it is filtered like any other request.
internal sealed class RenderQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    ILogger<RenderQueue> logger) : IRenderQueue
{
    public async Task<RenderWork?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (caller.OrganizationId is not null)
        {
            throw new InvalidOperationException("This queue has already claimed a job. Each job is claimed in a scope of its own.");
        }

        // One statement: the row is locked as it is chosen and a locked row is passed
        // over, so two workers asking at the same moment are given different jobs.
        var queued = nameof(RenderJobState.Queued);
        var validating = nameof(RenderJobState.Validating);
        var now = clock.GetUtcNow();
        var claimed = await database.Database
            .SqlQuery<ClaimedJob>($"""
                UPDATE "RenderJobs" SET "State" = {validating}, "UpdatedAt" = {now}
                WHERE "Id" = (
                    SELECT "Id" FROM "RenderJobs" WHERE "State" = {queued}
                    ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED)
                RETURNING "Id", "OrganizationId"
                """)
            .ToListAsync(cancellationToken);
        if (claimed is not [var mine]) return null;

        caller.Identify(mine.OrganizationId, memberId: null);
        var job = await database.RenderJobs.SingleAsync(j => j.Id == mine.Id, cancellationToken);
        var storyboard = await database.Storyboards.AsNoTracking().SingleAsync(s => s.Id == job.StoryboardId, cancellationToken);
        var shown = storyboard.Scenes.SelectMany(scene => scene.AssetIds).Distinct().ToList();
        var assets = await database.ProductAssets.AsNoTracking().Where(a => shown.Contains(a.Id)).ToListAsync(cancellationToken);
        return new RenderWork(job, storyboard, assets);
    }

    public async Task MoveAsync(RenderWork work, RenderJobState state, CancellationToken cancellationToken)
    {
        if (!work.Job.MoveTo(state, clock.GetUtcNow()))
        {
            throw new InvalidOperationException($"Render job {work.Job.Id} cannot go from {work.Job.State} to {state}.");
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(RenderWork work, string reason, CancellationToken cancellationToken)
    {
        // Whatever the failed attempt left unsaved is not part of the failure: the job is read again as it was saved.
        database.ChangeTracker.Clear();
        var job = await database.RenderJobs.SingleAsync(j => j.Id == work.Job.Id, cancellationToken);
        if (job.Fail(reason, clock.GetUtcNow())) await database.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(RenderWork work, Stream mp4, IReadOnlyCollection<Guid> uncutAssetIds, CancellationToken cancellationToken)
    {
        var job = work.Job;
        var now = clock.GetUtcNow();
        var video = new RenderedVideo(
            Guid.CreateVersion7(), job.OrganizationId, job.StoryboardId, job.Id,
            work.Storyboard.Scenes.Sum(scene => scene.DurationMs), mp4.Length, uncutAssetIds, now);
        if (!job.Complete(video.Id, now))
        {
            throw new InvalidOperationException($"Render job {job.Id} cannot be completed from {job.State}.");
        }
        database.RenderedVideos.Add(video);

        // The file first: a record never points at a file that is not there.
        await storage.PutAsync(video.StorageKey, mp4, RenderedVideo.ContentType, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(video.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", video.StorageKey);
            }
            throw;
        }
    }

    private sealed record ClaimedJob(Guid Id, Guid OrganizationId);
}
