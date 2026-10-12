namespace AffiVideo.Domain;

/// <summary>The tenant that owns products, projects and videos.</summary>
public sealed class Organization
{
    public const int NameMaxLength = 100;

    /// <summary>The most a Project may spend on generation unless an Owner says otherwise, in the currency of the rates.</summary>
    public const decimal DefaultGenerationCeiling = 5m;

    public Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Whether the Organization has the Affiliate Lab: Campaigns, Published Posts and what follows from them (ADR 0001).</summary>
    public bool AffiliateLabEnabled { get; private set; }

    /// <summary>
    /// The most each Project of the Organization may spend on generation: Stills, Clips and speech,
    /// rejected ones included. A generation that would pass it is refused.
    /// </summary>
    public decimal GenerationCeiling { get; private set; } = DefaultGenerationCeiling;

    public void Rename(string name) => Name = name;

    public void EnableAffiliateLab() => AffiliateLabEnabled = true;
}
