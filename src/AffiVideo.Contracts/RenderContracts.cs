using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>The work of rendering one Storyboard version. Its state is all the progress there is to show.</summary>
/// <param name="StoryboardId">The Storyboard version being rendered.</param>
/// <param name="Attempt">How many times a worker has taken the job. More than one when it was tried again.</param>
/// <param name="RetryAt">When a job that is queued to be tried again may next be taken. Otherwise absent.</param>
/// <param name="Failure">Why the job failed. Only a failed job has one.</param>
/// <param name="RenderedVideoId">What the job made. Only a completed job has one, and no longer once the Rendered Video is deleted.</param>
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

/// <summary>
/// The MP4 produced from one Storyboard version: 1080 by 1920, H.264 video and AAC audio.
/// It is ready for review until a member approves it, and can be downloaded once approved.
/// </summary>
/// <param name="RenderJobId">The job that made it.</param>
/// <param name="DurationMs">In milliseconds: the sum of the Scene durations.</param>
/// <param name="UncutAssetIds">The photos shown whole, on a card, because the Product could not be cut out of them cleanly.</param>
/// <param name="DrawnScenePositions">The Scenes that were drawn to make this video. Every other Scene was reused from an earlier render in which it was the same.</param>
/// <param name="ProductName">As the Product is named now.</param>
/// <param name="ProjectObjective">What the Project it was made in is called by.</param>
/// <param name="CreativeTemplate">Of the Variant it was made for.</param>
/// <param name="Hook">Of the Variant it was made for.</param>
/// <param name="StoryboardVersion">The version of the Variant's Storyboard that was rendered.</param>
/// <param name="ApprovedByMemberId">The member who approved it. Only an approved Rendered Video has one.</param>
/// <param name="NarrationAudioId">The narration mixed into the video, when the Variant had one as it was rendered. It may since have been replaced or removed.</param>
/// <param name="MusicAudioId">The music mixed into the video, when the Variant had one as it was rendered.</param>
/// <param name="MusicVolumePercent">How loud that music was mixed, from 0 to 100. Only a video with music has one.</param>
/// <param name="Flags">Why the video is Flagged for Review: one for each Withdrawn Fact it used, until a member clears it. Empty when it is not flagged.</param>
public sealed record RenderedVideoResponse(
    Guid Id,
    Guid StoryboardId,
    Guid RenderJobId,
    RenderedVideoState State,
    int DurationMs,
    long SizeInBytes,
    IReadOnlyList<Guid> UncutAssetIds,
    IReadOnlyList<int> DrawnScenePositions,
    DateTimeOffset CreatedAt,
    Guid ProductId,
    string ProductName,
    Guid ProjectId,
    string ProjectObjective,
    Guid VariantId,
    CreativeTemplate CreativeTemplate,
    string Hook,
    int StoryboardVersion,
    Guid? ApprovedByMemberId,
    string? ApprovedByEmail,
    DateTimeOffset? ApprovedAt,
    Guid? NarrationAudioId,
    Guid? MusicAudioId,
    int? MusicVolumePercent,
    IReadOnlyList<ReviewFlagResponse> Flags);
