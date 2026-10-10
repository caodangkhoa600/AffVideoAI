using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>The social accounts of the caller's Organization, kept to be chosen again.</summary>
public interface ISocialAccounts
{
    /// <returns>Null when the Organization already has that handle on that platform.</returns>
    Task<SocialAccount?> CreateAsync(SocialPlatform platform, string handle, CancellationToken cancellationToken);

    /// <summary>By platform, then by handle.</summary>
    Task<Page<SocialAccount>> ListAsync(PageRequest page, CancellationToken cancellationToken);
}

/// <summary>The affiliate links of the caller's Organization, kept to be chosen again.</summary>
public interface IAffiliateLinks
{
    /// <returns>Null when the Organization already has a link with that URL.</returns>
    Task<AffiliateLinkRecord?> CreateAsync(string url, string? label, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<Page<AffiliateLinkRecord>> ListAsync(PageRequest page, CancellationToken cancellationToken);
}

/// <param name="PublishedPostCount">How many Published Posts carry the link.</param>
public sealed record AffiliateLinkRecord(AffiliateLink Link, int PublishedPostCount);

/// <summary>
/// The Published Posts of the caller's Organization. A Published Post, and anything
/// one is recorded from, of another Organization is answered exactly as one that
/// does not exist. Recording one posts nothing: publishing is done by hand.
/// </summary>
public interface IPublishedPosts
{
    Task<PublishedPostOutcome> RecordAsync(NewPublishedPost post, CancellationToken cancellationToken);

    Task<PublishedPostRecord?> FindAsync(Guid postId, CancellationToken cancellationToken);

    /// <summary>The latest publication date first.</summary>
    Task<Page<PublishedPostRecord>> ListAsync(PublishedPostFilter filter, PageRequest page, CancellationToken cancellationToken);
}

public sealed record NewPublishedPost(Guid RenderedVideoId, Guid SocialAccountId, Guid? AffiliateLinkId, DateOnly PublishedOn, string Url);

/// <summary>What narrows the list of Published Posts. Whatever is left out narrows nothing.</summary>
/// <param name="CampaignId">Leaves the posts of Rendered Videos of the Variants the Campaign groups.</param>
public sealed record PublishedPostFilter(
    Guid? CampaignId, Guid? ProductId, Guid? VariantId, SocialPlatform? Platform, Guid? SocialAccountId);

/// <summary>A Published Post with the Rendered Video it shows and what that was made from, as those are now.</summary>
/// <param name="AffiliateLinkShared">Whether another Published Post carries the same affiliate link.</param>
public sealed record PublishedPostRecord(
    PublishedPost Post,
    SocialAccount Account,
    AffiliateLink? AffiliateLink,
    bool AffiliateLinkShared,
    Guid ProductId,
    string ProductName,
    Guid ProjectId,
    Guid VariantId,
    CreativeTemplate CreativeTemplate,
    string Hook);

/// <summary>
/// The Published Post as recorded; or what in the request names nothing of the
/// Organization, by field; or the reason it was refused, in words for the member.
/// </summary>
public sealed record PublishedPostOutcome(PublishedPostRecord? Record, IReadOnlyDictionary<string, string>? Unknown, string? Refused)
{
    public static PublishedPostOutcome Recorded(PublishedPostRecord record) => new(record, null, null);

    public static PublishedPostOutcome NoSuch(IReadOnlyDictionary<string, string> unknown) => new(null, unknown, null);

    public static PublishedPostOutcome Refuse(string reason) => new(null, null, reason);
}
