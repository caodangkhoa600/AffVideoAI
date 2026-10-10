namespace AffiVideo.Domain;

/// <summary>
/// One approved Rendered Video posted on one social account at one URL. The same
/// video on three accounts is three Published Posts. It is a record of what a
/// member did by hand: nothing here posts anything.
/// </summary>
public sealed class PublishedPost : IOwnedByOrganization
{
    // For the data-access layer, which fills the properties itself.
    private PublishedPost()
    {
    }

    public PublishedPost(
        Guid id, Guid organizationId, Guid renderedVideoId, Guid socialAccountId, Guid? affiliateLinkId,
        DateOnly publishedOn, string url, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        RenderedVideoId = renderedVideoId;
        SocialAccountId = socialAccountId;
        AffiliateLinkId = affiliateLinkId;
        PublishedOn = publishedOn;
        Url = url.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid RenderedVideoId { get; private set; }

    /// <summary>The account it was posted on, which also says the platform.</summary>
    public Guid SocialAccountId { get; private set; }

    /// <summary>The affiliate link it carries, when it carries one.</summary>
    public Guid? AffiliateLinkId { get; private set; }

    /// <summary>The day it was posted, as the member gives it.</summary>
    public DateOnly PublishedOn { get; private set; }

    /// <summary>Where it is. An Organization records a URL once.</summary>
    public string Url { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>An account on a platform that videos are posted on, kept so it can be chosen again and reported on.</summary>
public sealed class SocialAccount : IOwnedByOrganization
{
    public const int HandleMaxLength = 100;

    // For the data-access layer, which fills the properties itself.
    private SocialAccount()
    {
    }

    public SocialAccount(Guid id, Guid organizationId, SocialPlatform platform, string handle, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Platform = platform;
        Handle = handle.Trim();
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public SocialPlatform Platform { get; private set; }

    /// <summary>What the account is called on its platform, as the member typed it.</summary>
    public string Handle { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Where short videos are posted.</summary>
public enum SocialPlatform
{
    TikTok,
    Facebook,
    Instagram,
    YouTube,
    Shopee,
}

/// <summary>
/// A link an affiliate programme gave, kept so it can be chosen again and reported
/// on. Commission can be shown for a Published Post only while no other uses its link.
/// </summary>
public sealed class AffiliateLink : IOwnedByOrganization
{
    public const int LabelMaxLength = 200;

    // For the data-access layer, which fills the properties itself.
    private AffiliateLink()
    {
    }

    public AffiliateLink(Guid id, Guid organizationId, string url, string? label, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Url = url.Trim();
        Label = label?.Trim() ?? "";
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Url { get; private set; } = "";

    /// <summary>What the member calls it. Empty when it has no name of its own.</summary>
    public string Label { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
}
