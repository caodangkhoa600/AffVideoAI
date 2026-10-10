namespace AffiVideo.Domain;

/// <summary>The tenant that owns products, projects and videos.</summary>
public sealed class Organization
{
    public const int NameMaxLength = 100;

    public Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Whether the Organization has the Affiliate Lab: Campaigns, Published Posts and what follows from them (ADR 0001).</summary>
    public bool AffiliateLabEnabled { get; private set; }

    public void Rename(string name) => Name = name;

    public void EnableAffiliateLab() => AffiliateLabEnabled = true;
}
