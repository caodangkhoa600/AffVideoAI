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

// The queue is the Clips table, served as StillQueue serves the Stills: claiming,
// and failing the Clips whose lease has run out, are the queries here that look
// across Organizations; a claim identifies the caller as the Clip's Organization,
// and everything after it is filtered like any other request. A Clip that waits
// for its Still is not queued, so no worker ever takes one.
//
// A Clip that ends here, made or failed for good, leaves its production cost
// record, saved with what ended it.
internal sealed class ClipQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    IOptions<RenderQueueOptions> options,
    ILogger<ClipQueue> logger) : IClipQueue
{
    private readonly RenderQueueOptions _options = options.Value;

    public async Task<ClipWork?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (caller.OrganizationId is not null)
        {
            throw new InvalidOperationException("This queue has already claimed a Clip. Each is claimed in a scope of its own.");
        }

        var now = clock.GetUtcNow();
        await FailAbandonedAsync(now, cancellationToken);

        // One statement: the row is locked as it is chosen and a locked row is passed
        // over, so two workers asking at the same moment are given different Clips.
        var queued = nameof(ClipState.Queued);
        var generating = nameof(ClipState.Generating);
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now + _options.LeaseDuration;
        var claimed = await database.Database
            .SqlQuery<ClaimedClip>($"""
                UPDATE "Clips" SET
                    "State" = {generating}, "Attempt" = "Attempt" + 1, "AttemptStartedAt" = {now},
                    "LeaseId" = {leaseId}, "LeaseExpiresAt" = {leaseExpiresAt}, "UpdatedAt" = {now}
                WHERE "Id" = (
                    SELECT "Id" FROM "Clips" WHERE "State" = {queued} AND "AvailableAt" <= {now}
                    ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED)
                RETURNING "Id", "OrganizationId"
                """)
            .ToListAsync(cancellationToken);
        if (claimed is not [var mine]) return null;

        caller.Identify(mine.OrganizationId, memberId: null);
        var clip = await database.Clips.SingleAsync(c => c.Id == mine.Id, cancellationToken);
        // A Clip is queued only from an approved Still, and a Still is kept for as long as a Clip was made from it.
        var still = await database.Stills.AsNoTracking().SingleAsync(s => s.Id == clip.StillId, cancellationToken);
        return new ClipWork(clip, leaseId, still);
    }

    public async Task<bool> RenewAsync(ClipWork work, CancellationToken cancellationToken)
    {
        var leaseExpiresAt = clock.GetUtcNow() + _options.LeaseDuration;
        var renewed = await database.Database.ExecuteSqlAsync(
            $"""UPDATE "Clips" SET "LeaseExpiresAt" = {leaseExpiresAt} WHERE "Id" = {work.Clip.Id} AND "LeaseId" = {work.LeaseId}""",
            cancellationToken);
        return renewed == 1;
    }

    public async Task<bool> CompleteAsync(ClipWork work, string model, Stream mp4, CancellationToken cancellationToken)
    {
        var clip = work.Clip;
        var now = clock.GetUtcNow();
        if (!clip.Made(model, mp4.Length, now))
        {
            throw new InvalidOperationException($"Clip {clip.Id} cannot be made from {clip.State}.");
        }
        database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), clip, now));

        // The file first: a record never points at a file that is not there.
        await storage.PutAsync(clip.StorageKey, mp4, Clip.ContentType, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception notSaved)
        {
            try
            {
                await storage.DeleteAsync(clip.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", clip.StorageKey);
            }
            // Taken away since it was claimed: the video is no longer this worker's to keep.
            if (notSaved is DbUpdateConcurrencyException) return false;
            throw;
        }
    }

    public async Task FailAsync(
        ClipWork work, ClipFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken)
    {
        // Whatever the failed attempt left unsaved is not part of the failure: the Clip is read again as it was saved.
        database.ChangeTracker.Clear();
        var clip = await database.Clips.SingleOrDefaultAsync(c => c.Id == work.Clip.Id, cancellationToken);
        if (clip is null || clip.LeaseId != work.LeaseId) return;

        var now = clock.GetUtcNow();
        // Only what was not charged for is tried again without the member, and footage the provider refuses will be refused again.
        if (kind == ClipFailureKind.Technical && !billed && clip.Attempt < _options.MaxAttempts)
        {
            var wait = _options.RetryDelayAfter(clip.Attempt);
            logger.LogInformation(
                "Clip {ClipId} goes back to the queue after attempt {Attempt} of {MaxAttempts}, to be taken again in {Wait}",
                clip.Id, clip.Attempt, _options.MaxAttempts, wait);
            clip.Requeue(now + wait, now);
        }
        else
        {
            clip.Fail(kind, message, detail, billed, now);
            database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), clip, now));
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Taken away since it was read: the failure is no longer the Clip's to have.
        }
    }

    // As for a Still (ADR 0003): a Clip whose worker stopped does not go back to the queue, because nobody
    // knows whether the provider charged for what that worker asked of it. It is failed, taken to have been
    // charged for, and given its record.
    private async Task FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // The rows are locked as they are chosen, so of two workers that come by at the same
        // moment one fails a Clip and the other finds it failed.
        var generating = nameof(ClipState.Generating);
        var failed = nameof(ClipState.Failed);
        var workerLost = nameof(ClipFailureKind.WorkerLost);
        const string message =
            "The Clip was not made: the worker stopped while the video provider was making it. The provider may have charged for it, " +
            "so it is counted as paid for and was not asked for again. Generate again if you still want it.";
        var lost = await database.Database
            .SqlQuery<LostClip>($"""
                WITH lost AS (
                    SELECT "Id" FROM "Clips" WHERE "State" = {generating} AND "LeaseExpiresAt" < {now} FOR UPDATE)
                UPDATE "Clips" AS clip SET
                    "FailureKind" = {workerLost}, "FailureMessage" = {message},
                    "FailureDetail" = format('The lease of attempt %s ran out at %s and was not renewed.', clip."Attempt", clip."LeaseExpiresAt"),
                    "Billed" = TRUE, "State" = {failed}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now}
                FROM lost WHERE clip."Id" = lost."Id"
                RETURNING clip."Id" AS "ClipId", clip."OrganizationId", clip."ProductId", clip."Provider",
                    clip."EstimatedAmount", clip."Currency", clip."RatesVersion"
                """)
            .ToListAsync(cancellationToken);

        // The Clips are of any Organization, and this queue acts for none yet, so their records are written past the filter.
        foreach (var clip in lost)
        {
            var id = Guid.CreateVersion7();
            // Every column of the record, as its mapping in AffiVideoDbContext has them: a new one is added here too.
            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "GenerationCostRecords" (
                    "Id", "OrganizationId", "ProductId", "StillId", "ClipId", "Provider", "Model", "EstimatedAmount", "Currency", "RatesVersion", "RecordedAt")
                VALUES (
                    {id}, {clip.OrganizationId}, {clip.ProductId}, NULL, {clip.ClipId}, {clip.Provider}, NULL, {clip.EstimatedAmount},
                    {clip.Currency}, {clip.RatesVersion}, {now})
                """,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        if (lost.Count > 0) logger.LogWarning("{Failed} Clips whose lease had run out were failed", lost.Count);
    }

    private sealed record ClaimedClip(Guid Id, Guid OrganizationId);

    private sealed record LostClip(
        Guid ClipId, Guid OrganizationId, Guid ProductId, string Provider, decimal EstimatedAmount, string Currency, string RatesVersion);
}
