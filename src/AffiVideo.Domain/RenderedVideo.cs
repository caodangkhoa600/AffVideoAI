namespace AffiVideo.Domain;

/// <summary>The MP4 produced from one specific Storyboard version. Nothing about the file ever changes.</summary>
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
        int durationMs, long sizeInBytes, IEnumerable<Guid> uncutAssetIds, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        StoryboardId = storyboardId;
        RenderJobId = renderJobId;
        State = RenderedVideoState.ReadyForReview;
        DurationMs = durationMs;
        SizeInBytes = sizeInBytes;
        UncutAssetIds = [.. uncutAssetIds];
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

    public DateTimeOffset CreatedAt { get; private set; }

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
}
