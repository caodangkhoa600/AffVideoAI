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

/// <summary>An archived Product is kept, with everything made from it, but is out of the way of day-to-day work.</summary>
public enum ProductStatus
{
    Active,
    Archived,
}
