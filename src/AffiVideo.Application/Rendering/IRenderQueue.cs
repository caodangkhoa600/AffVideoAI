using AffiVideo.Domain;

namespace AffiVideo.Application.Rendering;

/// <summary>
/// The render jobs as a worker sees them: every Organization's, oldest first.
/// One instance serves one job, and from the moment it has claimed one it acts
/// for that job's Organization and no other.
/// <para>
/// A claimed job is the worker's for as long as its lease lasts, and the worker
/// keeps it by renewing the lease. A job whose lease has run out goes back to
/// the queue; one a member has cancelled is nobody's. Either way the worker that
/// had it is told <see cref="RenderStoppedException"/> when it next writes, and
/// nothing it writes is kept.
/// </para>
/// </summary>
public interface IRenderQueue
{
    /// <summary>
    /// Takes the job that has been queued longest, of those that may be taken now,
    /// and moves it to validating under a new lease. A job is given to one caller
    /// only, however many ask at the same moment. Before that, every job whose
    /// lease has run out is put back in the queue, or failed if it has been tried
    /// as often as a job is.
    /// </summary>
    /// <returns>Null when nothing is queued.</returns>
    Task<RenderWork?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves the end of the job's lease on. Unlike the rest, this may be asked of
    /// an instance other than the one that claimed the job, while that one works.
    /// </summary>
    /// <returns>False when the job is no longer this worker's, and the work on it should stop.</returns>
    Task<bool> RenewAsync(RenderWork work, CancellationToken cancellationToken);

    /// <summary>Moves the claimed job to its next stage.</summary>
    Task MoveAsync(RenderWork work, RenderJobState state, CancellationToken cancellationToken);

    /// <summary>
    /// Records that this attempt failed. The job is failed for good when trying
    /// again would not help or it has been tried as often as a job is; otherwise
    /// it goes back to the queue, to be taken again after a wait that doubles with each attempt.
    /// </summary>
    /// <param name="message">Why, in words for the member.</param>
    /// <param name="detail">What went wrong as the program reported it.</param>
    Task FailAsync(RenderWork work, RenderFailureCategory category, string message, string? detail, CancellationToken cancellationToken);

    /// <summary>Stores the MP4 as the Storyboard version's Rendered Video, ready for review, and completes the job.</summary>
    /// <param name="uncutAssetIds">The photos the video shows whole, on a card.</param>
    Task CompleteAsync(RenderWork work, Stream mp4, IReadOnlyCollection<Guid> uncutAssetIds, CancellationToken cancellationToken);
}

/// <summary>A claimed job and what it renders.</summary>
/// <param name="LeaseId">The lease this claim holds the job under.</param>
/// <param name="Storyboard">The version to render, with its Scenes.</param>
/// <param name="Assets">Those of the assets the Scenes show that the Product still has.</param>
public sealed record RenderWork(RenderJob Job, Guid LeaseId, Storyboard Storyboard, IReadOnlyList<ProductAsset> Assets);

/// <summary>
/// The job is no longer this worker's: a member cancelled it, it was deleted with
/// its Project, or its lease ran out and it went back to the queue.
/// </summary>
public sealed class RenderStoppedException(Guid jobId)
    : Exception($"Render job {jobId} is no longer this worker's: it was cancelled, or its lease ran out.");

/// <summary>How the queue treats a job that a worker has taken.</summary>
public sealed class RenderQueueOptions
{
    public const string Section = "RenderQueue";

    /// <summary>How long a job stays its worker's after the worker last renewed the lease.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often a worker renews the lease of a job it is working on. It is also
    /// how soon a worker learns that the job was cancelled.
    /// </summary>
    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many times a job is taken before it is failed for good: the first attempt and the retries.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How long a job waits before its second attempt. The wait doubles with each attempt after.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The wait before the attempt that follows this one: exponential backoff.</summary>
    /// <param name="attempt">The attempt that has just failed, counted from one.</param>
    public TimeSpan RetryDelayAfter(int attempt) => RetryBaseDelay * Math.Pow(2, Math.Max(0, attempt - 1));
}
