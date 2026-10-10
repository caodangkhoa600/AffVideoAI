using AffiVideo.Domain;

namespace AffiVideo.Application.Rendering;

/// <summary>
/// The render jobs of the caller's Organization. A Storyboard or a job of another
/// Organization is answered exactly as one that does not exist. Nothing here
/// renders: a job waits for the worker. What a job makes is in <see cref="IRenderedVideos"/>.
/// </summary>
public interface IRenders
{
    /// <summary>
    /// Queues a job that renders this Storyboard version, unless one was already
    /// queued for it with this idempotency key: then that job is the answer, whatever state it is in.
    /// </summary>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<SubmittedRender?> SubmitAsync(Guid projectId, Guid variantId, int version, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The jobs that render this Storyboard version, newest first. Null when the Variant has no such version.</summary>
    Task<Page<RenderJob>?> ListJobsAsync(Guid projectId, Guid variantId, int version, PageRequest page, CancellationToken cancellationToken);

    /// <summary>The Organization's jobs that have not ended, queued or being rendered, newest first.</summary>
    Task<Page<RenderJobInProgress>> ListJobsInProgressAsync(PageRequest page, CancellationToken cancellationToken);

    Task<RenderJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a job that is queued or that a worker is running. The worker stops
    /// when it next looks, and nothing it has made is kept.
    /// </summary>
    /// <returns>The job as it now is: cancelled, unless it had already ended otherwise. Null when there is no such job.</returns>
    Task<RenderJob?> CancelJobAsync(Guid jobId, CancellationToken cancellationToken);
}

/// <summary>A job that has not ended, with the Product, Project, Variant and Storyboard version it renders.</summary>
public sealed record RenderJobInProgress(
    RenderJob Job,
    Guid ProductId,
    string ProductName,
    Guid ProjectId,
    Guid VariantId,
    CreativeTemplate CreativeTemplate,
    string Hook,
    int StoryboardVersion);

/// <param name="Queued">False when the job was already there, queued by an earlier request with the same key.</param>
public sealed record SubmittedRender(RenderJob Job, bool Queued);
