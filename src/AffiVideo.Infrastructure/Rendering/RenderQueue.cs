using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Rendering;

// The queue is the RenderJobs table. Claiming, and putting back the jobs whose
// lease has run out, are the queries here that look across Organizations; a claim
// identifies the caller as the job's Organization, and everything after it is
// filtered like any other request.
internal sealed class RenderQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    IOptions<RenderQueueOptions> options,
    ILogger<RenderQueue> logger) : IRenderQueue
{
    private static readonly string[] Running =
        [.. Enum.GetValues<RenderJobState>().Where(state => state.IsRunning()).Select(state => state.ToString())];

    private readonly RenderQueueOptions _options = options.Value;

    public async Task<RenderWork?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (caller.OrganizationId is not null)
        {
            throw new InvalidOperationException("This queue has already claimed a job. Each job is claimed in a scope of its own.");
        }

        var now = clock.GetUtcNow();
        await ReturnAbandonedAsync(now, cancellationToken);

        // One statement: the row is locked as it is chosen and a locked row is passed
        // over, so two workers asking at the same moment are given different jobs.
        var queued = nameof(RenderJobState.Queued);
        var validating = nameof(RenderJobState.Validating);
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now + _options.LeaseDuration;
        var claimed = await database.Database
            .SqlQuery<ClaimedJob>($"""
                UPDATE "RenderJobs" SET
                    "State" = {validating}, "Attempt" = "Attempt" + 1,
                    "LeaseId" = {leaseId}, "LeaseExpiresAt" = {leaseExpiresAt}, "UpdatedAt" = {now}
                WHERE "Id" = (
                    SELECT "Id" FROM "RenderJobs" WHERE "State" = {queued} AND "AvailableAt" <= {now}
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
        return new RenderWork(job, leaseId, storyboard, assets);
    }

    public async Task<bool> RenewAsync(RenderWork work, CancellationToken cancellationToken)
    {
        // A cancelled job has no lease, and one that went back to the queue has none or another's.
        var leaseExpiresAt = clock.GetUtcNow() + _options.LeaseDuration;
        var renewed = await database.Database.ExecuteSqlAsync(
            $"""UPDATE "RenderJobs" SET "LeaseExpiresAt" = {leaseExpiresAt} WHERE "Id" = {work.Job.Id} AND "LeaseId" = {work.LeaseId}""",
            cancellationToken);
        return renewed == 1;
    }

    public async Task MoveAsync(RenderWork work, RenderJobState state, CancellationToken cancellationToken)
    {
        if (!work.Job.MoveTo(state, clock.GetUtcNow()))
        {
            throw new InvalidOperationException($"Render job {work.Job.Id} cannot go from {work.Job.State} to {state}.");
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RenderStoppedException(work.Job.Id);
        }
    }

    public async Task FailAsync(
        RenderWork work, RenderFailureCategory category, string message, string? detail, CancellationToken cancellationToken)
    {
        // Whatever the failed attempt left unsaved is not part of the failure: the job is read again as it was saved.
        database.ChangeTracker.Clear();
        var job = await database.RenderJobs.SingleOrDefaultAsync(j => j.Id == work.Job.Id, cancellationToken);
        if (job is null || job.LeaseId != work.LeaseId) return;

        var now = clock.GetUtcNow();
        // A Storyboard that cannot be rendered will not be rendered by trying again.
        if (category != RenderFailureCategory.InvalidInput && job.Attempt < _options.MaxAttempts)
        {
            var wait = _options.RetryDelayAfter(job.Attempt);
            logger.LogInformation(
                "Render job {JobId} goes back to the queue after attempt {Attempt} of {MaxAttempts}, to be taken again in {Wait}",
                job.Id, job.Attempt, _options.MaxAttempts, wait);
            job.Requeue(now + wait, now);
        }
        else
        {
            job.Fail(new RenderFailure(job.State, category, message, detail), now);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cancelled, or taken away, since it was read: the failure is no longer the job's to have.
        }
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
        catch (Exception notSaved)
        {
            try
            {
                await storage.DeleteAsync(video.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", video.StorageKey);
            }
            if (notSaved is DbUpdateConcurrencyException) throw new RenderStoppedException(job.Id);
            throw;
        }
    }

    // A worker that stops without a word leaves its job running, under a lease nobody
    // renews. Each such job goes back to the queue, to wait as a job that failed waits;
    // one that has been taken as often as a job is, is failed instead.
    private async Task ReturnAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var failed = nameof(RenderJobState.Failed);
        var lost = nameof(RenderFailureCategory.WorkerLost);
        var tried = _options.MaxAttempts == 1 ? "once" : $"{_options.MaxAttempts} times";
        var message = $"The video could not be rendered: the worker stopped before it had finished, and the job had been tried {tried}. Try rendering again.";
        var failedForGood = await database.Database.ExecuteSqlAsync(
            $"""
            UPDATE "RenderJobs" SET
                "FailureStage" = "State", "FailureCategory" = {lost}, "FailureMessage" = {message},
                "FailureDetail" = format('The lease of attempt %s ran out at %s and was not renewed.', "Attempt", "LeaseExpiresAt"),
                "State" = {failed}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now}
            WHERE "State" = ANY({Running}) AND "LeaseExpiresAt" < {now} AND "Attempt" >= {_options.MaxAttempts}
            """,
            cancellationToken);

        var queued = nameof(RenderJobState.Queued);
        var baseSeconds = _options.RetryBaseDelay.TotalSeconds;
        var returned = await database.Database.ExecuteSqlAsync(
            $"""
            UPDATE "RenderJobs" SET
                "State" = {queued}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now},
                "AvailableAt" = {now} + {baseSeconds} * power(2, GREATEST("Attempt" - 1, 0)) * interval '1 second'
            WHERE "State" = ANY({Running}) AND "LeaseExpiresAt" < {now} AND "Attempt" < {_options.MaxAttempts}
            """,
            cancellationToken);

        if (failedForGood + returned > 0)
        {
            logger.LogWarning(
                "{Returned} render jobs whose lease had run out went back to the queue, and {Failed} were failed", returned, failedForGood);
        }
    }

    private sealed record ClaimedJob(Guid Id, Guid OrganizationId);
}
