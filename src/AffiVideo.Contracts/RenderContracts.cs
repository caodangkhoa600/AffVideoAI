using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>The work of rendering one Storyboard version. Its state is all the progress there is to show.</summary>
/// <param name="StoryboardId">The Storyboard version being rendered.</param>
/// <param name="FailureReason">Why the job failed, in words for the member. Only a failed job has one.</param>
/// <param name="RenderedVideoId">What the job made. Only a completed job has one.</param>
/// <param name="UpdatedAt">When the state last changed.</param>
public sealed record RenderJobResponse(
    Guid Id,
    Guid StoryboardId,
    RenderJobState State,
    string? FailureReason,
    Guid? RenderedVideoId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The MP4 produced from one Storyboard version: 1080 by 1920, H.264 video and AAC audio.</summary>
/// <param name="RenderJobId">The job that made it.</param>
/// <param name="DurationMs">In milliseconds: the sum of the Scene durations.</param>
/// <param name="UncutAssetIds">The photos shown whole, on a card, because the Product could not be cut out of them cleanly.</param>
public sealed record RenderedVideoResponse(
    Guid Id,
    Guid StoryboardId,
    Guid RenderJobId,
    RenderedVideoState State,
    int DurationMs,
    long SizeInBytes,
    IReadOnlyList<Guid> UncutAssetIds,
    DateTimeOffset CreatedAt);
