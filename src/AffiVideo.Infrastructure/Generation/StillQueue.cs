using AffiVideo.Application;
using AffiVideo.Application.Generation;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Generation;

// The queue is the Stills table, served as RenderQueue serves the render jobs:
// claiming, and failing the Stills whose lease has run out, are the queries
// here that look across Organizations; a claim identifies the caller as the Still's
// Organization, and everything after it is filtered like any other request.
//
// A Still that ends here, made or failed for good, leaves its production cost
// record, saved with what ended it.
internal sealed class StillQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    IOptions<RenderQueueOptions> options,
    ILogger<StillQueue> logger) : IStillQueue
{
    private readonly RenderQueueOptions _options = options.Value;

    public async Task<StillWork?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (caller.OrganizationId is not null)
        {
            throw new InvalidOperationException("This queue has already claimed a Still. Each is claimed in a scope of its own.");
        }

        var now = clock.GetUtcNow();
        await FailAbandonedAsync(now, cancellationToken);

        // One statement: the row is locked as it is chosen and a locked row is passed
        // over, so two workers asking at the same moment are given different Stills.
        var queued = nameof(StillState.Queued);
        var generating = nameof(StillState.Generating);
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now + _options.LeaseDuration;
        var claimed = await database.Database
            .SqlQuery<ClaimedStill>($"""
                UPDATE "Stills" SET
                    "State" = {generating}, "Attempt" = "Attempt" + 1, "AttemptStartedAt" = {now},
                    "LeaseId" = {leaseId}, "LeaseExpiresAt" = {leaseExpiresAt}, "UpdatedAt" = {now}
                WHERE "Id" = (
                    SELECT "Id" FROM "Stills" WHERE "State" = {queued} AND "AvailableAt" <= {now}
                    ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED)
                RETURNING "Id", "OrganizationId"
                """)
            .ToListAsync(cancellationToken);
        if (claimed is not [var mine]) return null;

        caller.Identify(mine.OrganizationId, memberId: null);
        var still = await database.Stills.SingleAsync(s => s.Id == mine.Id, cancellationToken);
        return new StillWork(still, leaseId);
    }

    public async Task<bool> RenewAsync(StillWork work, CancellationToken cancellationToken)
    {
        var leaseExpiresAt = clock.GetUtcNow() + _options.LeaseDuration;
        var renewed = await database.Database.ExecuteSqlAsync(
            $"""UPDATE "Stills" SET "LeaseExpiresAt" = {leaseExpiresAt} WHERE "Id" = {work.Still.Id} AND "LeaseId" = {work.LeaseId}""",
            cancellationToken);
        return renewed == 1;
    }

    public async Task<bool> CompleteAsync(StillWork work, string model, Stream png, CancellationToken cancellationToken)
    {
        var still = work.Still;
        var now = clock.GetUtcNow();
        if (!still.Made(model, png.Length, now))
        {
            throw new InvalidOperationException($"Still {still.Id} cannot be made from {still.State}.");
        }
        database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), still, now));

        // The file first: a record never points at a file that is not there.
        await storage.PutAsync(still.StorageKey, png, Still.ContentType, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception notSaved)
        {
            try
            {
                await storage.DeleteAsync(still.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", still.StorageKey);
            }
            // Taken away since it was claimed: the picture is no longer this worker's to keep.
            if (notSaved is DbUpdateConcurrencyException) return false;
            throw;
        }
    }

    public async Task FailAsync(
        StillWork work, StillFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken)
    {
        // Whatever the failed attempt left unsaved is not part of the failure: the Still is read again as it was saved.
        database.ChangeTracker.Clear();
        var still = await database.Stills.SingleOrDefaultAsync(s => s.Id == work.Still.Id, cancellationToken);
        if (still is null || still.LeaseId != work.LeaseId) return;

        var now = clock.GetUtcNow();
        // Only what was not charged for is tried again without the member, and a picture the provider refuses will be refused again.
        if (kind == StillFailureKind.Technical && !billed && still.Attempt < _options.MaxAttempts)
        {
            var wait = _options.RetryDelayAfter(still.Attempt);
            logger.LogInformation(
                "Still {StillId} goes back to the queue after attempt {Attempt} of {MaxAttempts}, to be taken again in {Wait}",
                still.Id, still.Attempt, _options.MaxAttempts, wait);
            still.Requeue(now + wait, now);
        }
        else
        {
            still.Fail(kind, message, detail, billed, now);
            database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), still, now));
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Taken away since it was read: the failure is no longer the Still's to have.
        }
    }

    // A worker that stops without a word leaves its Still being made, under a lease nobody renews.
    // Unlike a render job it does not go back to the queue: nobody knows whether the provider charged
    // for what that worker asked of it, and a paid generation is never made again without the member
    // (ADR 0003). It is failed, taken to have been charged for, and given its record.
    private async Task FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // The rows are locked as they are chosen, so of two workers that come by at the same
        // moment one fails a Still and the other finds it failed.
        var generating = nameof(StillState.Generating);
        var failed = nameof(StillState.Failed);
        var workerLost = nameof(StillFailureKind.WorkerLost);
        const string message =
            "The picture was not made: the worker stopped while the image provider was making it. The provider may have charged for it, " +
            "so it is counted as paid for and was not asked for again. Generate again if you still want it.";
        var lost = await database.Database
            .SqlQuery<LostStill>($"""
                WITH lost AS (
                    SELECT "Id" FROM "Stills" WHERE "State" = {generating} AND "LeaseExpiresAt" < {now} FOR UPDATE)
                UPDATE "Stills" AS still SET
                    "FailureKind" = {workerLost}, "FailureMessage" = {message},
                    "FailureDetail" = format('The lease of attempt %s ran out at %s and was not renewed.', still."Attempt", still."LeaseExpiresAt"),
                    "Billed" = TRUE, "State" = {failed}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now}
                FROM lost WHERE still."Id" = lost."Id"
                RETURNING still."Id" AS "StillId", still."OrganizationId", still."ProductId", still."Provider",
                    still."EstimatedAmount", still."Currency", still."RatesVersion"
                """)
            .ToListAsync(cancellationToken);

        // The Stills are of any Organization, and this queue acts for none yet, so their records are written past the filter.
        foreach (var still in lost)
        {
            var id = Guid.CreateVersion7();
            // Every column of the record, as its mapping in AffiVideoDbContext has them: a new one is added here too.
            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "GenerationCostRecords" (
                    "Id", "OrganizationId", "ProductId", "StillId", "ClipId", "Provider", "Model", "EstimatedAmount", "Currency", "RatesVersion", "RecordedAt")
                VALUES (
                    {id}, {still.OrganizationId}, {still.ProductId}, {still.StillId}, NULL, {still.Provider}, NULL, {still.EstimatedAmount},
                    {still.Currency}, {still.RatesVersion}, {now})
                """,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        if (lost.Count > 0) logger.LogWarning("{Failed} Stills whose lease had run out were failed", lost.Count);
    }

    private sealed record ClaimedStill(Guid Id, Guid OrganizationId);

    private sealed record LostStill(
        Guid StillId, Guid OrganizationId, Guid ProductId, string Provider, decimal EstimatedAmount, string Currency, string RatesVersion);
}
