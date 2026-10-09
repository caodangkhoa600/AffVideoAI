namespace AffiVideo.Domain;

/// <summary>
/// The Flagged for Review mark one Withdrawn Fact puts on a Storyboard version or
/// a Rendered Video that used it. It is never stored: work is flagged for as long
/// as a Fact it used is Withdrawn and no member has cleared that flag.
/// </summary>
/// <param name="Text">The Fact's text as the work used it.</param>
public sealed record ReviewFlag(Guid FactId, string Text, DateTimeOffset WithdrawnAt);

/// <summary>
/// A member's clearing of the flag one Withdrawn Fact put on one Storyboard
/// version or one Rendered Video, after reviewing it. A Fact withdrawn later
/// flags the same work again.
/// </summary>
public sealed class ClearedFlag : IOwnedByOrganization
{
    // For the data-access layer, which fills the properties itself.
    private ClearedFlag()
    {
    }

    private ClearedFlag(Guid organizationId, Guid factId, Guid? storyboardId, Guid? renderedVideoId, Guid memberId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        OrganizationId = organizationId;
        FactId = factId;
        StoryboardId = storyboardId;
        RenderedVideoId = renderedVideoId;
        ClearedByMemberId = memberId;
        ClearedAt = now;
    }

    public static ClearedFlag OnStoryboard(Storyboard storyboard, Guid factId, Guid memberId, DateTimeOffset now) =>
        new(storyboard.OrganizationId, factId, storyboard.Id, null, memberId, now);

    public static ClearedFlag OnRenderedVideo(RenderedVideo video, Guid factId, Guid memberId, DateTimeOffset now) =>
        new(video.OrganizationId, factId, null, video.Id, memberId, now);

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Withdrawn Fact whose flag was cleared.</summary>
    public Guid FactId { get; private set; }

    /// <summary>The Storyboard version the flag was on, when it was on one.</summary>
    public Guid? StoryboardId { get; private set; }

    /// <summary>The Rendered Video the flag was on, when it was on one.</summary>
    public Guid? RenderedVideoId { get; private set; }

    public Guid ClearedByMemberId { get; private set; }

    public DateTimeOffset ClearedAt { get; private set; }
}
