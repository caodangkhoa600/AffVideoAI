using AffiVideo.Application.Generation;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Domain;
using AffiVideo.Worker.Rendering;
using Microsoft.Extensions.Options;

namespace AffiVideo.Worker.Generation;

/// <summary>
/// Has the speech provider say the queued narration text, oldest first, one piece
/// at a time. Each piece has a scope of its own, which acts for that speech's
/// Organization and no other, and is kept by renewing its lease for as long as the
/// provider takes. Nothing here decides to spend: all speech in the queue was confirmed by a member.
/// </summary>
internal sealed class SpeechWorker(
    IServiceScopeFactory scopes,
    ITextToSpeech voice,
    IOptions<RenderingOptions> options,
    IOptions<RenderQueueOptions> queueOptions,
    ILogger<SpeechWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Making speech with the {Provider} speech provider", voice.Provider);
        while (!stoppingToken.IsCancellationRequested)
        {
            var made = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var queue = scope.ServiceProvider.GetRequiredService<ISpeechQueue>();
                if (await queue.ClaimAsync(stoppingToken) is { } work)
                {
                    made = true;
                    logger.LogInformation("Making speech {SpeechId}, attempt {Attempt}", work.Speech.Id, work.Speech.Attempt);
                    await MakeWhileHeldAsync(queue, work, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // The database is away, or speech could not even be marked as failed. The worker carries on.
                logger.LogError(exception, "The queue of speech could not be served");
                made = false;
            }

            if (!made) await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    // The provider is waited for only while the speech is this worker's.
    private async Task MakeWhileHeldAsync(ISpeechQueue queue, SpeechWork work, CancellationToken stoppingToken)
    {
        using var held = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewing = KeepLeaseAsync(work, held);
        try
        {
            switch (await voice.SpeakAsync(work.Speech.Text, work.Speech.Language, work.Speech.Voice, held.Token))
            {
                case SpeechGeneration.Made made:
                    await KeepAsync(queue, work, made, stoppingToken);
                    break;
                case SpeechGeneration.Refused refused:
                    await queue.FailAsync(
                        work, SpeechFailureKind.Refused,
                        $"The speech provider would not say this text: {refused.Reason} Change the Scene's narration text, then generate again.",
                        refused.Reason, refused.Billed, stoppingToken);
                    break;
                case SpeechGeneration.Failed failed:
                    await queue.FailAsync(
                        work, SpeechFailureKind.Technical,
                        "The speech could not be made: something went wrong at the speech provider. Ask for it again.",
                        failed.Detail, failed.Billed, stoppingToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (held.IsCancellationRequested)
        {
            // The worker is stopping, or the lease was lost: the speech is failed as paid for when its lease runs out.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Speech {SpeechId} could not be made", work.Speech.Id);
            await queue.FailAsync(
                work, SpeechFailureKind.Technical,
                "The speech could not be made: something went wrong in the worker. Ask for it again.",
                exception.ToString(), billed: false, stoppingToken);
        }
        finally
        {
            await held.CancelAsync();
            await renewing;
        }
    }

    // The provider has made the speech, and so has charged for it. If it cannot be kept, the speech
    // fails as paid for: asking the provider again would be paying again without being asked.
    private async Task KeepAsync(ISpeechQueue queue, SpeechWork work, SpeechGeneration.Made made, CancellationToken stoppingToken)
    {
        try
        {
            using var wav = new MemoryStream(made.Wav);
            if (!await queue.CompleteAsync(work, made.Model, made.DurationMs, wav, stoppingToken))
            {
                logger.LogInformation("Speech {SpeechId} was no longer this worker's when it was made; the sound was not kept", work.Speech.Id);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The sound of speech {SpeechId} was made and could not be kept", work.Speech.Id);
            await queue.FailAsync(
                work, SpeechFailureKind.Technical,
                "The speech was made and could not be kept: something went wrong in the worker. It is counted as paid for and was " +
                "not asked for again. Ask for it again if you still want it.",
                exception.ToString(), billed: true, stoppingToken);
        }
    }

    private async Task KeepLeaseAsync(SpeechWork work, CancellationTokenSource held)
    {
        using var timer = new PeriodicTimer(queueOptions.Value.LeaseRenewalInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(held.Token)) return;

                // A scope of its own: the speech's scope is busy with the speech.
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ISpeechQueue>().RenewAsync(work, held.Token)) continue;

                logger.LogInformation("Speech {SpeechId} is no longer this worker's; stopping work on it", work.Speech.Id);
                await held.CancelAsync();
                return;
            }
            catch (OperationCanceledException) when (held.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The database is away. The lease is tried again at the next tick.
                logger.LogWarning(exception, "The lease of speech {SpeechId} could not be renewed", work.Speech.Id);
            }
        }
    }
}
