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

// The queue is the Speech table, served as StillQueue serves the Stills: claiming,
// and failing the speech whose lease has run out, are the queries here that look
// across Organizations; a claim identifies the caller as the speech's Organization,
// and everything after it is filtered like any other request.
//
// Speech that ends here, made or failed for good, leaves its production cost
// record, saved with what ended it.
internal sealed class SpeechQueue(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    IOptions<RenderQueueOptions> options,
    ILogger<SpeechQueue> logger) : ISpeechQueue
{
    private readonly RenderQueueOptions _options = options.Value;

    public async Task<SpeechWork?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (caller.OrganizationId is not null)
        {
            throw new InvalidOperationException("This queue has already claimed speech. Each piece is claimed in a scope of its own.");
        }

        var now = clock.GetUtcNow();
        await FailAbandonedAsync(now, cancellationToken);

        // One statement: the row is locked as it is chosen and a locked row is passed
        // over, so two workers asking at the same moment are given different speech.
        var queued = nameof(SpeechState.Queued);
        var generating = nameof(SpeechState.Generating);
        var leaseId = Guid.NewGuid();
        var leaseExpiresAt = now + _options.LeaseDuration;
        var claimed = await database.Database
            .SqlQuery<ClaimedSpeech>($"""
                UPDATE "Speech" SET
                    "State" = {generating}, "Attempt" = "Attempt" + 1, "AttemptStartedAt" = {now},
                    "LeaseId" = {leaseId}, "LeaseExpiresAt" = {leaseExpiresAt}, "UpdatedAt" = {now}
                WHERE "Id" = (
                    SELECT "Id" FROM "Speech" WHERE "State" = {queued} AND "AvailableAt" <= {now}
                    ORDER BY "CreatedAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED)
                RETURNING "Id", "OrganizationId"
                """)
            .ToListAsync(cancellationToken);
        if (claimed is not [var mine]) return null;

        caller.Identify(mine.OrganizationId, memberId: null);
        var speech = await database.Speech.SingleAsync(s => s.Id == mine.Id, cancellationToken);
        return new SpeechWork(speech, leaseId);
    }

    public async Task<bool> RenewAsync(SpeechWork work, CancellationToken cancellationToken)
    {
        var leaseExpiresAt = clock.GetUtcNow() + _options.LeaseDuration;
        var renewed = await database.Database.ExecuteSqlAsync(
            $"""UPDATE "Speech" SET "LeaseExpiresAt" = {leaseExpiresAt} WHERE "Id" = {work.Speech.Id} AND "LeaseId" = {work.LeaseId}""",
            cancellationToken);
        return renewed == 1;
    }

    public async Task<bool> CompleteAsync(SpeechWork work, string model, int durationMs, Stream wav, CancellationToken cancellationToken)
    {
        var speech = work.Speech;
        var now = clock.GetUtcNow();
        if (!speech.Made(model, durationMs, wav.Length, now))
        {
            throw new InvalidOperationException($"Speech {speech.Id} cannot be made from {speech.State}.");
        }
        database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), speech, now));

        // The file first: a record never points at a file that is not there.
        await storage.PutAsync(speech.StorageKey, wav, Speech.ContentType, cancellationToken);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception notSaved)
        {
            try
            {
                await storage.DeleteAsync(speech.StorageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", speech.StorageKey);
            }
            // Taken away since it was claimed: the sound is no longer this worker's to keep.
            if (notSaved is DbUpdateConcurrencyException) return false;
            throw;
        }
    }

    public async Task FailAsync(
        SpeechWork work, SpeechFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken)
    {
        // Whatever the failed attempt left unsaved is not part of the failure: the speech is read again as it was saved.
        database.ChangeTracker.Clear();
        var speech = await database.Speech.SingleOrDefaultAsync(s => s.Id == work.Speech.Id, cancellationToken);
        if (speech is null || speech.LeaseId != work.LeaseId) return;

        var now = clock.GetUtcNow();
        // Only what was not charged for is tried again without the member, and a text the provider refuses will be refused again.
        if (kind == SpeechFailureKind.Technical && !billed && speech.Attempt < _options.MaxAttempts)
        {
            var wait = _options.RetryDelayAfter(speech.Attempt);
            logger.LogInformation(
                "Speech {SpeechId} goes back to the queue after attempt {Attempt} of {MaxAttempts}, to be taken again in {Wait}",
                speech.Id, speech.Attempt, _options.MaxAttempts, wait);
            speech.Requeue(now + wait, now);
        }
        else
        {
            speech.Fail(kind, message, detail, billed, now);
            database.GenerationCostRecords.Add(new GenerationCostRecord(Guid.CreateVersion7(), speech, now));
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Taken away since it was read: the failure is no longer the speech's to have.
        }
    }

    // As for a Still: a worker that stops without a word leaves its speech being made, under a lease nobody
    // renews. It does not go back to the queue: nobody knows whether the provider charged for what that worker
    // asked of it, and a paid generation is never made again without the member (ADR 0003). It is failed,
    // taken to have been charged for, and given its record.
    private async Task FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // The rows are locked as they are chosen, so of two workers that come by at the same
        // moment one fails the speech and the other finds it failed.
        var generating = nameof(SpeechState.Generating);
        var failed = nameof(SpeechState.Failed);
        var workerLost = nameof(SpeechFailureKind.WorkerLost);
        const string message =
            "The speech was not made: the worker stopped while the speech provider was making it. The provider may have charged for it, " +
            "so it is counted as paid for and was not asked for again. Generate again if you still want it.";
        var lost = await database.Database
            .SqlQuery<LostSpeech>($"""
                WITH lost AS (
                    SELECT "Id" FROM "Speech" WHERE "State" = {generating} AND "LeaseExpiresAt" < {now} FOR UPDATE)
                UPDATE "Speech" AS speech SET
                    "FailureKind" = {workerLost}, "FailureMessage" = {message},
                    "FailureDetail" = format('The lease of attempt %s ran out at %s and was not renewed.', speech."Attempt", speech."LeaseExpiresAt"),
                    "Billed" = TRUE, "State" = {failed}, "LeaseId" = NULL, "LeaseExpiresAt" = NULL, "UpdatedAt" = {now}
                FROM lost WHERE speech."Id" = lost."Id"
                RETURNING speech."Id" AS "SpeechId", speech."OrganizationId", speech."ProductId", speech."Provider",
                    speech."EstimatedAmount", speech."Currency", speech."RatesVersion"
                """)
            .ToListAsync(cancellationToken);

        // The speech is of any Organization, and this queue acts for none yet, so its records are written past the filter.
        foreach (var speech in lost)
        {
            var id = Guid.CreateVersion7();
            // Every column of the record, as its mapping in AffiVideoDbContext has them: a new one is added here too.
            await database.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "GenerationCostRecords" (
                    "Id", "OrganizationId", "ProductId", "StillId", "ClipId", "SpeechId", "Provider", "Model", "EstimatedAmount", "Currency",
                    "RatesVersion", "RecordedAt")
                VALUES (
                    {id}, {speech.OrganizationId}, {speech.ProductId}, NULL, NULL, {speech.SpeechId}, {speech.Provider}, NULL,
                    {speech.EstimatedAmount}, {speech.Currency}, {speech.RatesVersion}, {now})
                """,
                cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);

        if (lost.Count > 0) logger.LogWarning("{Failed} pieces of speech whose lease had run out were failed", lost.Count);
    }

    private sealed record ClaimedSpeech(Guid Id, Guid OrganizationId);

    private sealed record LostSpeech(
        Guid SpeechId, Guid OrganizationId, Guid ProductId, string Provider, decimal EstimatedAmount, string Currency, string RatesVersion);
}
