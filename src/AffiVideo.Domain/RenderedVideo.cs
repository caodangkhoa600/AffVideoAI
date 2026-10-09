namespace AffiVideo.Domain;

/// <summary>
/// The MP4 produced from one specific Storyboard version. Nothing about the file
/// ever changes; the only thing that does is that a member approves it.
/// </summary>
public sealed class RenderedVideo : IOwnedByOrganization
{
    public const int Width = 1080;
    public const int Height = 1920;
    public const int FramesPerSecond = 30;

    /// <summary>What every Rendered Video is: H.264 video and AAC audio in an MP4.</summary>
    public const string ContentType = "video/mp4";

    // For the data-access layer, which fills the properties itself.
    private RenderedVideo()
    {
    }

    public RenderedVideo(
        Guid id, Guid organizationId, Guid storyboardId, Guid renderJobId,
        int durationMs, long sizeInBytes, IEnumerable<Guid> uncutAssetIds, IEnumerable<int> drawnScenePositions,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        StoryboardId = storyboardId;
        RenderJobId = renderJobId;
        State = RenderedVideoState.ReadyForReview;
        DurationMs = durationMs;
        SizeInBytes = sizeInBytes;
        UncutAssetIds = [.. uncutAssetIds];
        DrawnScenePositions = [.. drawnScenePositions];
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid StoryboardId { get; private set; }

    /// <summary>The job that made it.</summary>
    public Guid RenderJobId { get; private set; }

    public RenderedVideoState State { get; private set; }

    /// <summary>The sum of the Scene durations, in milliseconds.</summary>
    public int DurationMs { get; private set; }

    public long SizeInBytes { get; private set; }

    /// <summary>
    /// The photos shown whole, on a card, because the Product could not be cut out
    /// of them cleanly. Every other photo in the video is a cut-out.
    /// </summary>
    public Guid[] UncutAssetIds { get; private set; } = [];

    /// <summary>
    /// The positions of the Scenes that were drawn to make this video. Every other
    /// Scene was reused from an earlier render in which it was the same.
    /// </summary>
    public int[] DrawnScenePositions { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The member who approved it. Only an approved Rendered Video has one.</summary>
    public Guid? ApprovedByMemberId { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Whether the MP4 may leave as a file. It can be previewed in either state.</summary>
    public bool CanBeDownloaded => State == RenderedVideoState.Approved;

    /// <summary>Marks the video as fit to publish, in this member's name.</summary>
    /// <returns>False, with nothing changed, when it is already approved.</returns>
    public bool Approve(Guid memberId, DateTimeOffset now)
    {
        if (State != RenderedVideoState.ReadyForReview) return false;

        State = RenderedVideoState.Approved;
        ApprovedByMemberId = memberId;
        ApprovedAt = now;
        return true;
    }

    /// <summary>
    /// Where the file is in object storage. It starts with the Organization, so
    /// everything one Organization has stored is under one prefix of its own.
    /// </summary>
    public string StorageKey => $"organizations/{OrganizationId}/rendered-videos/{Id}.mp4";
}

public enum RenderedVideoState
{
    /// <summary>Rendered and checked, and waiting for a member to watch it.</summary>
    ReadyForReview,

    /// <summary>A member has watched it and marked it as fit to publish. It can be downloaded.</summary>
    Approved,
}
