using AffiVideo.Domain;

namespace AffiVideo.Application.Generation;

/// <summary>
/// The Stills waiting to be made, as a worker sees them: every Organization's,
/// oldest first. It is the render queue's twin, with the same lease, attempts and
/// waits (<see cref="Rendering.RenderQueueOptions"/>): one instance serves one Still,
/// and from the moment it has claimed one it acts for that Still's Organization and no other.
/// </summary>
public interface IStillQueue
{
    /// <summary>
    /// Takes the Still that has been queued longest, of those that may be taken now, under a new
    /// lease. Before that, every Still whose lease has run out is failed, as charged for: nobody knows
    /// whether the provider charged, and it is not asked again without the member.
    /// </summary>
    /// <returns>Null when nothing is queued.</returns>
    Task<StillWork?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>Moves the end of the Still's lease on. This may be asked of an instance other than the one that claimed it.</summary>
    /// <returns>False when the Still is no longer this worker's, and the work on it should stop.</returns>
    Task<bool> RenewAsync(StillWork work, CancellationToken cancellationToken);

    /// <summary>Stores the picture and makes the Still ready for review, with its production cost record.</summary>
    /// <returns>False, with nothing kept, when the Still is no longer this worker's.</returns>
    Task<bool> CompleteAsync(StillWork work, string model, Stream png, CancellationToken cancellationToken);

    /// <summary>
    /// Records that this attempt made nothing. Only a technical failure the provider did not charge
    /// for goes back to the queue, to be taken again after a wait, as often as a job is tried.
    /// Anything else fails the Still for good, with its production cost record.
    /// </summary>
    /// <param name="message">Why, in words for the member.</param>
    /// <param name="detail">What went wrong as the provider or the program reported it.</param>
    Task FailAsync(StillWork work, StillFailureKind kind, string message, string? detail, bool billed, CancellationToken cancellationToken);
}

/// <summary>A claimed Still.</summary>
/// <param name="LeaseId">The lease this claim holds the Still under.</param>
public sealed record StillWork(Still Still, Guid LeaseId);
