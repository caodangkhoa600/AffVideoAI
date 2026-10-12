using AffiVideo.Domain;

namespace AffiVideo.Application.Generation;

/// <summary>
/// The Clips waiting to be made, as a worker sees them: every Organization's,
/// oldest first. It is the twin of <see cref="IStillQueue"/>, with the same lease,
/// attempts and waits (<see cref="Rendering.RenderQueueOptions"/>): one instance serves one Clip,
/// and from the moment it has claimed one it acts for that Clip's Organization and no other.
/// A Clip that waits for its Still is not in the queue.
/// </summary>
public interface IClipQueue
{
    /// <summary>
    /// Takes the Clip that has been queued longest, of those that may be taken now, under a new
    /// lease. Before that, every Clip whose lease has run out is failed, as charged for: nobody knows
    /// whether the provider charged, and it is not asked again without the member.
    /// </summary>
    /// <returns>Null when nothing is queued.</returns>
    Task<ClipWork?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>Moves the end of the Clip's lease on. This may be asked of an instance other than the one that claimed it.</summary>
    /// <returns>False when the Clip is no longer this worker's, and the work on it should stop.</returns>
    Task<bool> RenewAsync(ClipWork work, CancellationToken cancellationToken);

    /// <summary>Stores the video and makes the Clip ready for review, with its production cost record.</summary>
    /// <param name="mp4">The video as it is to be kept: with no sound in it.</param>
    /// <returns>False, with nothing kept, when the Clip is no longer this worker's.</returns>
    Task<bool> CompleteAsync(ClipWork work, string model, Stream mp4, CancellationToken cancellationToken);

    /// <summary>
    /// Records that this attempt made nothing. Only a technical failure the provider did not charge
    /// for goes back to the queue, to be taken again after a wait, as often as a job is tried.
    /// Anything else fails the Clip for good, with its production cost record.
    /// </summary>
    /// <param name="message">Why, in words for the member.</param>
    /// <param name="detail">What went wrong as the provider or the program reported it.</param>
    Task FailAsync(ClipWork work, ClipFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken);
}

/// <summary>A claimed Clip.</summary>
/// <param name="LeaseId">The lease this claim holds the Clip under.</param>
/// <param name="Still">The approved Still the Clip is made from.</param>
public sealed record ClipWork(Clip Clip, Guid LeaseId, Still Still);
