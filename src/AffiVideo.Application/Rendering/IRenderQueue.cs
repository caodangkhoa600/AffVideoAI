using AffiVideo.Domain;

namespace AffiVideo.Application.Rendering;

/// <summary>
/// The render jobs as the worker sees them: every Organization's, oldest first.
/// One instance serves one job, and from the moment it has claimed one it acts
/// for that job's Organization and no other.
/// </summary>
public interface IRenderQueue
{
    /// <summary>
    /// Takes the job that has been queued longest and moves it to validating. A
    /// job is given to one caller only, however many ask at the same moment.
    /// </summary>
    /// <returns>Null when nothing is queued.</returns>
    Task<RenderWork?> ClaimAsync(CancellationToken cancellationToken);

    /// <summary>Moves the claimed job to its next stage.</summary>
    Task MoveAsync(RenderWork work, RenderJobState state, CancellationToken cancellationToken);

    /// <param name="reason">Why, in words for the member.</param>
    Task FailAsync(RenderWork work, string reason, CancellationToken cancellationToken);

    /// <summary>Stores the MP4 as the Storyboard version's Rendered Video, ready for review, and completes the job.</summary>
    /// <param name="uncutAssetIds">The photos the video shows whole, on a card.</param>
    Task CompleteAsync(RenderWork work, Stream mp4, IReadOnlyCollection<Guid> uncutAssetIds, CancellationToken cancellationToken);
}

/// <summary>A claimed job and what it renders.</summary>
/// <param name="Storyboard">The version to render, with its Scenes.</param>
/// <param name="Assets">Those of the assets the Scenes show that the Product still has.</param>
public sealed record RenderWork(RenderJob Job, Storyboard Storyboard, IReadOnlyList<ProductAsset> Assets);
