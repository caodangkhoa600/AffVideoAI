namespace AffiVideo.Domain;

/// <summary>A thing being advertised. Its URLs are text a member typed in: nothing ever fetches them.</summary>
public sealed class Product : IOwnedByOrganization
{
    public const int NameMaxLength = 200;
    public const int CategoryMaxLength = 100;
    public const int BrandMaxLength = 100;
    public const int DescriptionMaxLength = 4000;
    public const int TargetAudienceMaxLength = 500;
    public const int UrlMaxLength = 2048;
    public const int CurrencyLength = 3;
    public const int MaxTags = 20;
    public const int TagMaxLength = 50;

    /// <summary>Digits a price keeps after the decimal point. Anything finer is refused, not rounded.</summary>
    public const int PriceDecimals = 2;
    // Few enough digits that a price survives the trip through a browser, where every number is a double.
    public const int PricePrecision = 15;

    public const int ResearchNotesMaxLength = 4000;

    /// <summary>Digits a commission rate keeps after the decimal point.</summary>
    public const int CommissionRateDecimals = 2;
    // Up to 100.00.
    public const int CommissionRatePrecision = 5;

    // For the data-access layer, which fills the properties itself.
    private Product()
    {
    }

    public Product(Guid id, Guid organizationId, ProductDetails details, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Status = ProductStatus.Active;
        CreatedAt = createdAt;
        Change(details, createdAt);
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = "";

    public string Category { get; private set; } = "";

    public string Brand { get; private set; } = "";

    public string Description { get; private set; } = "";

    /// <summary>Always together with <see cref="Currency"/>, or both absent.</summary>
    public decimal? Price { get; private set; }

    /// <summary>An ISO 4217 code such as VND.</summary>
    public string? Currency { get; private set; }

    public string OriginalUrl { get; private set; } = "";

    public string? AffiliateUrl { get; private set; }

    public string TargetAudience { get; private set; } = "";

    public string[] Tags { get; private set; } = [];

    public ProductStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the Product was put on the Affiliate Lab's shortlist. Absent while it is not on it.</summary>
    public DateTimeOffset? ShortlistedAt { get; private set; }

    /// <summary>What the Affiliate Lab found out about the Product. Empty when there is nothing.</summary>
    public string ResearchNotes { get; private set; } = "";

    /// <summary>The share of an order the programme pays, from 0 to 100, when that is how it pays and it is known.</summary>
    public decimal? CommissionRatePercent { get; private set; }

    /// <summary>What the programme pays for an order, when it pays a fixed amount and it is known. Always together with <see cref="CommissionCurrency"/>.</summary>
    public decimal? CommissionAmount { get; private set; }

    public string? CommissionCurrency { get; private set; }

    public void Change(ProductDetails details, DateTimeOffset now)
    {
        Name = details.Name.Trim();
        Category = details.Category.Trim();
        Brand = details.Brand.Trim();
        Description = details.Description.Trim();
        Price = details.Price;
        Currency = details.Currency;
        OriginalUrl = details.OriginalUrl.Trim();
        AffiliateUrl = string.IsNullOrWhiteSpace(details.AffiliateUrl) ? null : details.AffiliateUrl.Trim();
        TargetAudience = details.TargetAudience.Trim();
        // The same tag in another letter case is the same tag; the first spelling is kept.
        Tags = details.Tags
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        UpdatedAt = now;
    }

    /// <summary>Replaces what the Affiliate Lab keeps about the Product. Nothing else about it changes.</summary>
    public void ChangeLabDetails(LabProductDetails details, DateTimeOffset now)
    {
        // The moment it was first shortlisted is kept while it stays on the shortlist.
        ShortlistedAt = details.Shortlisted ? ShortlistedAt ?? now : null;
        ResearchNotes = details.ResearchNotes?.Trim() ?? "";
        CommissionRatePercent = details.CommissionRatePercent;
        CommissionAmount = details.CommissionAmount;
        CommissionCurrency = details.CommissionCurrency;
        UpdatedAt = now;
    }

    /// <summary>Archiving a Product that is already archived changes nothing.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (Status == ProductStatus.Archived) return;
        Status = ProductStatus.Archived;
        UpdatedAt = now;
    }
}

/// <summary>Everything a member types in about a Product.</summary>
public sealed record ProductDetails(
    string Name,
    string Category,
    string Brand,
    string Description,
    decimal? Price,
    string? Currency,
    string OriginalUrl,
    string? AffiliateUrl,
    string TargetAudience,
    IReadOnlyList<string> Tags);

/// <summary>What the Affiliate Lab keeps about a Product: the shortlist, its research notes, and the commission as a rate or as an amount.</summary>
public sealed record LabProductDetails(
    bool Shortlisted,
    string? ResearchNotes,
    decimal? CommissionRatePercent,
    decimal? CommissionAmount,
    string? CommissionCurrency);

/// <summary>An archived Product is kept, with everything made from it, but is out of the way of day-to-day work.</summary>
public enum ProductStatus
{
    Active,
    Archived,
}
