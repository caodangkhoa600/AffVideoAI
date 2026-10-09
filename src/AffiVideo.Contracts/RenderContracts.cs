using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>The work of rendering one Storyboard version. Its state is all the progress there is to show.</summary>
/// <param name="StoryboardId">The Storyboard version being rendered.</param>
/// <param name="Attempt">How many times a worker has taken the job. More than one when it was tried again.</param>
/// <param name="RetryAt">When a job that is queued to be tried again may next be taken. Otherwise absent.</param>
/// <param name="Failure">Why the job failed. Only a failed job has one.</param>
/// <param name="RenderedVideoId">What the job made. Only a completed job has one.</param>
/// <param name="UpdatedAt">When the state last changed.</param>
public sealed record RenderJobResponse(
    Guid Id,
    Guid StoryboardId,
    RenderJobState State,
    int Attempt,
    DateTimeOffset? RetryAt,
    RenderFailureResponse? Failure,
    Guid? RenderedVideoId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Why a render job failed.</summary>
/// <param name="Stage">The stage the job was in.</param>
/// <param name="Category">The kind of thing that went wrong. A job that failed on <c>InvalidInput</c> was not tried again.</param>
/// <param name="Message">In words for the member.</param>
/// <param name="Detail">What went wrong as the program reported it, for whoever looks into it. Not for showing to the member.</param>
public sealed record RenderFailureResponse(RenderJobState Stage, RenderFailureCategory Category, string Message, string? Detail);

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
