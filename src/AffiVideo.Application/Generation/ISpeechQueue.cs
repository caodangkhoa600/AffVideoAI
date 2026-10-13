using AffiVideo.Domain;

namespace AffiVideo.Application.Generation;

/// <summary>
/// The speech waiting to be made, as a worker sees it: every Organization's,
/// oldest first. It is the queue of Stills' twin, with the same lease, attempts and
/// waits (<see cref="Rendering.RenderQueueOptions"/>): one instance serves one piece of
/// speech, and from the moment it has claimed one it acts for that speech's Organization and no other.
/// </summary>
public interface ISpeechQueue
{
    /// <summary>
    /// Takes the speech that has been queued longest, of what may be taken now, under a new lease.
    /// Before that, all speech whose lease has run out is failed, as charged for: nobody knows
    /// whether the provider charged, and it is not asked again without the member.
    /// </summary>
    /// <returns>Null when nothing is queued.</returns>
    Task<SpeechWork?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>Moves the end of the speech's lease on. This may be asked of an instance other than the one that claimed it.</summary>
    /// <returns>False when the speech is no longer this worker's, and the work on it should stop.</returns>
    Task<bool> RenewAsync(SpeechWork work, CancellationToken cancellationToken);

    /// <summary>Stores the sound and marks the speech as made, with its production cost record.</summary>
    /// <param name="durationMs">How long the sound lasts, in milliseconds.</param>
    /// <returns>False, with nothing kept, when the speech is no longer this worker's.</returns>
    Task<bool> CompleteAsync(SpeechWork work, string model, int durationMs, Stream wav, CancellationToken cancellationToken);

    /// <summary>
    /// Records that this attempt made nothing. Only a technical failure the provider did not charge
    /// for goes back to the queue, to be taken again after a wait, as often as a job is tried.
    /// Anything else fails the speech for good, with its production cost record.
    /// </summary>
    /// <param name="message">Why, in words for the member.</param>
    /// <param name="detail">What went wrong as the provider or the program reported it.</param>
    Task FailAsync(SpeechWork work, SpeechFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken);
}

/// <summary>A claimed piece of speech.</summary>
/// <param name="LeaseId">The lease this claim holds the speech under.</param>
public sealed record SpeechWork(Speech Speech, Guid LeaseId);
