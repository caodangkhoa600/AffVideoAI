using AffiVideo.Domain;

namespace AffiVideo.Application.Rendering;

/// <summary>
/// The render jobs and Rendered Videos of the caller's Organization. A Storyboard,
/// a job or a video of another Organization is answered exactly as one that does
/// not exist. Nothing here renders: a job waits for the worker.
/// </summary>
public interface IRenders
{
    /// <summary>Queues a job that renders this Storyboard version.</summary>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<RenderJob?> SubmitAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken);

    /// <summary>The jobs that render this Storyboard version, newest first. Null when the Variant has no such version.</summary>
    Task<Page<RenderJob>?> ListJobsAsync(Guid projectId, Guid variantId, int version, PageRequest page, CancellationToken cancellationToken);

    Task<RenderJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<RenderedVideo?> FindVideoAsync(Guid videoId, CancellationToken cancellationToken);

    /// <summary>The MP4, for the caller to dispose. Null when there is no such Rendered Video.</summary>
    Task<RenderedVideoContent?> OpenVideoAsync(Guid videoId, CancellationToken cancellationToken);
}

public sealed record RenderedVideoContent(RenderedVideo Video, Stream Content);
