using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// What a member types in about a Product, when creating it and when editing it.
/// The URLs are kept as text; nothing fetches them.
/// </summary>
public sealed record ProductRequest(
    [Required, StringLength(Product.NameMaxLength)] string Name,
    [Required, StringLength(Product.CategoryMaxLength)] string Category,
    [Required, StringLength(Product.BrandMaxLength)] string Brand,
    [Required, StringLength(Product.DescriptionMaxLength)] string Description,
    [Required, StringLength(Product.UrlMaxLength), WebAddress] string OriginalUrl,
    [Required, StringLength(Product.TargetAudienceMaxLength)] string TargetAudience,
    [Price] decimal? Price = null,
    [CurrencyCode] string? Currency = null,
    [StringLength(Product.UrlMaxLength), WebAddress] string? AffiliateUrl = null,
    [Tags] IReadOnlyList<string>? Tags = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Price is not null && string.IsNullOrEmpty(Currency))
        {
            yield return new ValidationResult("A price needs a currency.", [nameof(Currency)]);
        }
        if (Price is null && !string.IsNullOrEmpty(Currency))
        {
            yield return new ValidationResult("A currency needs a price.", [nameof(Price)]);
        }
    }
}

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Category,
    string Brand,
    string Description,
    decimal? Price,
    string? Currency,
    string OriginalUrl,
    string? AffiliateUrl,
    string TargetAudience,
    IReadOnlyList<string> Tags,
    ProductStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A photo or the logo of a Product. The image itself is at
/// <c>/api/v1/products/{productId}/assets/{id}/content</c>, always as a PNG.
/// </summary>
/// <param name="Width">In pixels, as stored: a photo taken sideways has been turned upright.</param>
/// <param name="SizeInBytes">Of the stored PNG, not of the file that was sent.</param>
public sealed record ProductAssetResponse(
    Guid Id,
    Guid ProductId,
    ProductAssetKind Kind,
    int Width,
    int Height,
    long SizeInBytes,
    DateTimeOffset CreatedAt);

/// <summary>
/// An absolute http or https address. It is only checked for shape, never requested.
/// A blank one is no address at all, which is for [Required] to refuse.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class WebAddressAttribute() : ValidationAttribute("Enter a full web address starting with http:// or https://.")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string text
            && (string.IsNullOrWhiteSpace(text) || IsWebAddress(text)));

    private static bool IsWebAddress(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var address)
        && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);
}

/// <summary>Zero or more, with no more decimal places or digits than are stored.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PriceAttribute() : ValidationAttribute("Enter a price of zero or more, with at most two decimal places.")
{
    private static readonly decimal Limit = (decimal)Math.Pow(10, Product.PricePrecision - Product.PriceDecimals);

    public override bool IsValid(object? value) =>
        value is null
        || (value is decimal price
            && price >= 0
            && price < Limit
            && decimal.Round(price, Product.PriceDecimals) == price);
}

/// <summary>Three capital letters, as ISO 4217 writes a currency.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class CurrencyCodeAttribute() : ValidationAttribute("Use a three-letter currency code in capitals, such as VND.")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string { Length: Product.CurrencyLength } code && code.All(letter => letter is >= 'A' and <= 'Z'));
}

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class TagsAttribute() : ValidationAttribute(
    $"Use at most {Product.MaxTags} tags, each of at most {Product.TagMaxLength} characters.")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is IReadOnlyList<string> tags
            && tags.All(tag => tag is not null && tag.Trim().Length <= Product.TagMaxLength)
            // Counted as they are kept: blanks dropped, and a repeat in another letter case once.
            && tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= Product.MaxTags);
}
