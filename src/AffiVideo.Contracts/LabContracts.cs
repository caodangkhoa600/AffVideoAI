using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// What the Affiliate Lab keeps about a Product: whether it is on the shortlist, the
/// research notes, and the commission the programme pays, as a rate or as an amount,
/// when it is known.
/// </summary>
/// <param name="CommissionRatePercent">From 0 to 100, with at most two decimal places.</param>
/// <param name="CommissionAmount">For each order, always with <paramref name="CommissionCurrency"/>.</param>
public sealed record LabProductRequest(
    bool Shortlisted,
    [StringLength(Product.ResearchNotesMaxLength)] string? ResearchNotes = null,
    [Percent] decimal? CommissionRatePercent = null,
    [Price] decimal? CommissionAmount = null,
    [CurrencyCode] string? CommissionCurrency = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CommissionAmount is not null && string.IsNullOrEmpty(CommissionCurrency))
        {
            yield return new ValidationResult("A commission amount needs a currency.", [nameof(CommissionCurrency)]);
        }
        if (CommissionAmount is null && !string.IsNullOrEmpty(CommissionCurrency))
        {
            yield return new ValidationResult("A currency needs a commission amount.", [nameof(CommissionAmount)]);
        }
        if (CommissionAmount is not null && CommissionRatePercent is not null)
        {
            yield return new ValidationResult("Enter the commission as a rate or as an amount, not both.", [nameof(CommissionAmount)]);
        }
    }
}

/// <summary>A Product as the Affiliate Lab sees it. It is the same Product as everywhere else.</summary>
/// <param name="ResearchNotes">Empty when there are none.</param>
public sealed record LabProductResponse(
    Guid ProductId,
    string Name,
    string Brand,
    string Category,
    ProductStatus Status,
    bool Shortlisted,
    string ResearchNotes,
    decimal? CommissionRatePercent,
    decimal? CommissionAmount,
    string? CommissionCurrency);

public sealed record CampaignRequest([Required, StringLength(Campaign.NameMaxLength)] string Name);

/// <param name="VariantCount">How many Variants the Campaign groups.</param>
public sealed record CampaignResponse(
    Guid Id,
    string Name,
    CampaignStatus Status,
    int VariantCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A Variant a Campaign groups, with the Project and the Product it belongs to. The Campaign does not own it.</summary>
/// <param name="ProductName">As the Product is named now.</param>
/// <param name="AddedAt">When it was added to the Campaign.</param>
public sealed record CampaignVariantResponse(
    Guid VariantId,
    Guid ProjectId,
    Guid ProductId,
    string ProductName,
    CreativeTemplate CreativeTemplate,
    string Hook,
    DateTimeOffset AddedAt);

/// <summary>From 0 to 100, with at most two decimal places.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class PercentAttribute() : ValidationAttribute("Enter a rate from 0 to 100, with at most two decimal places.")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is decimal rate
            && rate >= 0
            && rate <= 100
            && decimal.Round(rate, Product.CommissionRateDecimals) == rate);
}
