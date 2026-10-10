namespace AffiVideo.Domain;

/// <summary>
/// The running totals for a Published Post as of one moment, from one named
/// source. A total that is null is unknown, which is not zero. A snapshot is
/// never changed: a wrong figure is corrected by a newer snapshot.
/// </summary>
public sealed class PerformanceSnapshot : IOwnedByOrganization
{
    // For the data-access layer, which fills the properties itself.
    private PerformanceSnapshot()
    {
    }

    public PerformanceSnapshot(
        Guid id, Guid organizationId, Guid publishedPostId, DateTimeOffset takenAt, PerformanceTotals totals,
        PerformanceSource source, DateTimeOffset recordedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        PublishedPostId = publishedPostId;
        // The moment, whatever clock the member read it on.
        TakenAt = takenAt.ToUniversalTime();
        Views = totals.Views;
        Likes = totals.Likes;
        Comments = totals.Comments;
        Shares = totals.Shares;
        Clicks = totals.Clicks;
        Source = source;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid PublishedPostId { get; private set; }

    /// <summary>The moment the totals apply to: when they were read off the platform.</summary>
    public DateTimeOffset TakenAt { get; private set; }

    public long? Views { get; private set; }

    public long? Likes { get; private set; }

    public long? Comments { get; private set; }

    public long? Shares { get; private set; }

    public long? Clicks { get; private set; }

    public PerformanceSource Source { get; private set; }

    /// <summary>When the snapshot was entered.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public long? Total(PerformanceMetric metric) => metric switch
    {
        PerformanceMetric.Views => Views,
        PerformanceMetric.Likes => Likes,
        PerformanceMetric.Comments => Comments,
        PerformanceMetric.Shares => Shares,
        PerformanceMetric.Clicks => Clicks,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    /// <summary>
    /// The totals that are lower here than in the snapshot before this one. A running
    /// total does not usually go down, so each is likely a typing mistake, here or
    /// there. A total either snapshot does not know is not compared.
    /// </summary>
    public IReadOnlyList<PerformanceMetric> LowerThan(PerformanceSnapshot? previous) =>
        previous is null
            ? []
            : Enum.GetValues<PerformanceMetric>().Where(metric => Total(metric) < previous.Total(metric)).ToList();
}

/// <summary>The running totals of one moment. Null is unknown.</summary>
public sealed record PerformanceTotals(long? Views, long? Likes, long? Comments, long? Shares, long? Clicks);

/// <summary>What a Performance Snapshot counts.</summary>
public enum PerformanceMetric
{
    Views,
    Likes,
    Comments,
    Shares,
    Clicks,
}

/// <summary>Where the figures of a Performance Snapshot came from.</summary>
public enum PerformanceSource
{
    /// <summary>A member typed them in.</summary>
    Manual,
}
