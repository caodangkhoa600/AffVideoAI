using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AffiVideo.Infrastructure.Rendering;

// The queue is the RenderJobs table. Claiming, and putting back the jobs whose
// lease has run out, are the queries here that look across Organizations; a claim
// identifies the caller as the job's Organization, and everything after it is
// filtered like any other request.
//
// Every attempt that ends here leaves a production cost record, saved with what
// ended it: the Rendered Video, the failure, or the return to the queue.
internal sealed class RenderQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    IOptions<RenderQueueOptions> options,
    IOptions<ProductionCostOptions> costs,
    ILogger<RenderQueue> logger) : IRenderQueue
{
    private static readonly string[] Running =
        [.. Enum.GetValues<RenderJobState>().Where(state => state.IsRunning()).Select(state => state.ToString())];

    // The index that keeps an attempt to one production cost record.
    private const string OneRecordForAnAttempt = "IX_ProductionCostRecords_RenderJobId_Attempt";

    // The worker itself renders: no other provider does yet.
    private const RenderProvider Provider = RenderProvider.Local;

    private readonly RenderQueueOptions _options = options.Value;
    private readonly RenderRates _rates = costs.Value.RatesOf(Provider);

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
                    "State" = {validating}, "Attempt" = "Attempt" + 1, "AttemptStartedAt" = {now},
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
        var audio = await database.VariantAudio.AsNoTracking().Where(a => a.VariantId == storyboard.VariantId).ToListAsync(cancellationToken);
        var productId = await (
            from variant in database.Variants
            join project in database.Projects on variant.ProjectId equals project.Id
            where variant.Id == storyboard.VariantId
            select project.ProductId).SingleAsync(cancellationToken);
        return new RenderWork(job, leaseId, storyboard, assets, [.. audio.OrderBy(a => a.Kind)], productId);
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
        // Tried again or not, the attempt was made, and took what it took.
        database.ProductionCostRecords.Add(CostOf(work, job, RenderAttemptOutcome.Failed, now));

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception notSaved) when (notSaved is DbUpdateConcurrencyException || AttemptAlreadyRecorded(notSaved))
        {
            // Cancelled, or taken away, since it was read: the failure is no longer the job's to have.
        }
    }

    public async Task CompleteAsync(
        RenderWork work, Stream mp4, IReadOnlyCollection<Guid> uncutAssetIds, IReadOnlyCollection<int> drawnScenePositions,
        CancellationToken cancellationToken)
    {
        var job = work.Job;
        var now = clock.GetUtcNow();
        var video = new RenderedVideo(
            Guid.CreateVersion7(), job.OrganizationId, job.StoryboardId, job.Id,
            work.Storyboard.Scenes.Sum(scene => scene.DurationMs), mp4.Length, uncutAssetIds, drawnScenePositions, work.Audio, now);
        if (!job.Complete(video.Id, now))
        {
            throw new InvalidOperationException($"Render job {job.Id} cannot be completed from {job.State}.");
        }
        database.RenderedVideos.Add(video);
        database.ProductionCostRecords.Add(CostOf(work, job, RenderAttemptOutcome.Completed, now));

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
            if (notSaved is DbUpdateConcurrencyException || AttemptAlreadyRecorded(notSaved)) throw new RenderStoppedException(job.Id);
            throw;
        }
    }

    // The job's lease ran out and the queue took the job back between its being read and saved
    // here: the attempt has the record the queue wrote for it, and nothing of this save was kept.
    private static bool AttemptAlreadyRecorded(Exception notSaved) =>
        notSaved is DbUpdateException
        {
            InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneRecordForAnAttempt },
        };

    // The record of the attempt the job is at, which ends now.
    private ProductionCostRecord CostOf(RenderWork work, RenderJob job, RenderAttemptOutcome outcome, DateTimeOffset now) => new(
        Guid.CreateVersion7(), job.OrganizationId, work.ProductId, job.Id, job.Attempt, outcome, Provider,
        ProductionCosts.CountTechniques(work.Storyboard.Scenes), now - (job.AttemptStartedAt ?? now), _rates, now);

    // A worker that stops without a word leaves its job running, under a lease nobody
    // renews. Each such job goes back to the queue, to wait as a job that failed waits;
    // one that has been taken as often as a job is, is failed instead. Either way the
    // attempt is over, and its production cost record is written with the change.
    private async Task ReturnAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // In each statement the rows are locked as they are chosen, so of two workers that
        // come by at the same moment one returns a job and the other finds it returned.
        var failed = nameof(RenderJobState.Failed);
        var workerLost = nameof(RenderFailureCategory.WorkerLost);
        var tried = _options.MaxAttempts == 1 ? "once" : $"{_options.MaxAttempts} times";
        var message = $"The video could not be rendered: the worker stopped before it had finished, and the job had been tried {tried}. Try rendering again.";
        var failedForGood = await database.Database
            .SqlQuery<LostAttempt>($"""
                WITH lost AS (
                    SELECT "Id", "LeaseExpiresAt" AS "LostAt" FROM "RenderJobs"
                    WHERE "State" = ANY({Running}) AND "LeaseExpiresAt" < {now} AND "Attempt" >= {_options.MaxAttempts}
                    FOR UPDATE)
                UPDATE "RenderJobs" AS job SET
                    "FailureStage" = job."State", "FailureCategory" = {workerLost}, "FailureMessage" = {message},
                    "FailureDetail" = format('The lease of attempt %s ran out at %s and was not renewed.', job."Attempt", job."LeaseExpiresAt"),
                    "State" = {failed}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now}
                FROM lost WHERE job."Id" = lost."Id"
                RETURNING job."Id" AS "JobId", job."OrganizationId", job."StoryboardId", job."Attempt", job."AttemptStartedAt", lost."LostAt"
                """)
            .ToListAsync(cancellationToken);

        var queued = nameof(RenderJobState.Queued);
        var baseSeconds = _options.RetryBaseDelay.TotalSeconds;
        var returned = await database.Database
            .SqlQuery<LostAttempt>($"""
                WITH lost AS (
                    SELECT "Id", "LeaseExpiresAt" AS "LostAt" FROM "RenderJobs"
                    WHERE "State" = ANY({Running}) AND "LeaseExpiresAt" < {now} AND "Attempt" < {_options.MaxAttempts}
                    FOR UPDATE)
                UPDATE "RenderJobs" AS job SET
                    "State" = {queued}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now},
                    "AvailableAt" = {now} + {baseSeconds} * power(2, GREATEST(job."Attempt" - 1, 0)) * interval '1 second'
                FROM lost WHERE job."Id" = lost."Id"
                RETURNING job."Id" AS "JobId", job."OrganizationId", job."StoryboardId", job."Attempt", job."AttemptStartedAt", lost."LostAt"
                """)
            .ToListAsync(cancellationToken);

        await RecordLostAsync([.. failedForGood, .. returned], now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (failedForGood.Count + returned.Count > 0)
        {
            logger.LogWarning(
                "{Returned} render jobs whose lease had run out went back to the queue, and {Failed} were failed",
                returned.Count, failedForGood.Count);
        }
    }

    // The attempts that ended with nobody there to say so. They are of any Organization, and
    // this queue acts for none yet, so their records are read and written past the filter.
    private async Task RecordLostAsync(IReadOnlyCollection<LostAttempt> lost, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (lost.Count == 0) return;

        var storyboardIds = lost.Select(attempt => attempt.StoryboardId).Distinct().ToList();
        var storyboards = await database.Storyboards.IgnoreQueryFilters().AsNoTracking()
            .Where(storyboard => storyboardIds.Contains(storyboard.Id))
            .ToDictionaryAsync(storyboard => storyboard.Id, cancellationToken);
        var productOf = await (
                from storyboard in database.Storyboards.IgnoreQueryFilters()
                join variant in database.Variants on storyboard.VariantId equals variant.Id
                join project in database.Projects on variant.ProjectId equals project.Id
                where storyboardIds.Contains(storyboard.Id)
                select new { storyboard.Id, project.ProductId })
            .ToDictionaryAsync(found => found.Id, found => found.ProductId, cancellationToken);

        foreach (var attempt in lost)
        {
            // The worker was last known to have the job when its lease ran out: the attempt is taken to have lasted until then.
            var cost = new ProductionCostRecord(
                Guid.CreateVersion7(), attempt.OrganizationId, productOf[attempt.StoryboardId], attempt.JobId, attempt.Attempt,
                RenderAttemptOutcome.Failed, Provider, ProductionCosts.CountTechniques(storyboards[attempt.StoryboardId].Scenes),
                attempt.LostAt - (attempt.AttemptStartedAt ?? attempt.LostAt), _rates, now);
            var outcome = cost.Outcome.ToString();
            var provider = cost.Provider.ToString();
            var techniqueCounts = TechniqueCountsJson.Write(cost.TechniqueCounts);
            // Every column of the record, as its mapping in AffiVideoDbContext has them: a new one is added here too.
            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "ProductionCostRecords" (
                    "Id", "OrganizationId", "ProductId", "RenderJobId", "Attempt", "Outcome", "Provider", "TechniqueCounts",
                    "DurationMs", "EstimatedAmount", "Currency", "RatesVersion", "RecordedAt")
                VALUES (
                    {cost.Id}, {cost.OrganizationId}, {cost.ProductId}, {cost.RenderJobId}, {cost.Attempt}, {outcome}, {provider},
                    {techniqueCounts}::jsonb, {cost.DurationMs}, {cost.EstimatedAmount}, {cost.Currency}, {cost.RatesVersion}, {cost.RecordedAt})
                """,
                cancellationToken);
        }
    }

    private sealed record ClaimedJob(Guid Id, Guid OrganizationId);

    /// <param name="LostAt">When the lease the attempt held the job under ran out.</param>
    private sealed record LostAttempt(
        Guid JobId, Guid OrganizationId, Guid StoryboardId, int Attempt, DateTimeOffset? AttemptStartedAt, DateTimeOffset LostAt);
}
