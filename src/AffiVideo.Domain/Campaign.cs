namespace AffiVideo.Domain;

/// <summary>
/// A grouping of Variants across Products for one experiment. It references
/// Variants and never owns them: archiving a Campaign, or taking a Variant out of
/// it, leaves the Variant as it was.
/// </summary>
public sealed class Campaign : IOwnedByOrganization
{
    public const int NameMaxLength = 200;

    // For the data-access layer, which fills the properties itself.
    private Campaign()
    {
    }

    public Campaign(Guid id, Guid organizationId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name.Trim();
        Status = CampaignStatus.Active;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = "";

    public CampaignStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = name.Trim();
        UpdatedAt = now;
    }

    /// <summary>Archiving a Campaign that is already archived changes nothing.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (Status == CampaignStatus.Archived) return;
        Status = CampaignStatus.Archived;
        UpdatedAt = now;
    }
}

/// <summary>An archived Campaign is kept, with the Variants it groups, but is out of the way of day-to-day work.</summary>
public enum CampaignStatus
{
    Active,
    Archived,
}

/// <summary>That a Campaign groups a Variant. Removing it removes nothing else.</summary>
public sealed class CampaignVariant(Guid campaignId, Guid variantId, Guid organizationId, DateTimeOffset addedAt) : IOwnedByOrganization
{
    public Guid CampaignId { get; private set; } = campaignId;

    public Guid VariantId { get; private set; } = variantId;

    public Guid OrganizationId { get; private set; } = organizationId;

    public DateTimeOffset AddedAt { get; private set; } = addedAt;
}
