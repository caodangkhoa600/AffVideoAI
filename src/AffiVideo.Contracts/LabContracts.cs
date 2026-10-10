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

/// <param name="Handle">What the account is called on its platform.</param>
public sealed record SocialAccountRequest(
    SocialPlatform Platform,
    [Required, StringLength(SocialAccount.HandleMaxLength)] string Handle);

public sealed record SocialAccountResponse(Guid Id, SocialPlatform Platform, string Handle, DateTimeOffset CreatedAt);

/// <param name="Label">What the member calls the link, when it has a name of its own.</param>
public sealed record AffiliateLinkRequest(
    [Required, StringLength(Product.UrlMaxLength), WebAddress] string Url,
    [StringLength(AffiliateLink.LabelMaxLength)] string? Label = null);

/// <param name="Label">Empty when the link has no name of its own.</param>
/// <param name="PublishedPostCount">
/// How many Published Posts carry the link. Commission can be shown for a Published Post only when this is 1.
/// </param>
public sealed record AffiliateLinkResponse(Guid Id, string Url, string Label, int PublishedPostCount, DateTimeOffset CreatedAt);

/// <summary>That an approved Rendered Video was posted, by hand, on an account at a URL.</summary>
/// <param name="PublishedOn">The day it was posted.</param>
/// <param name="Url">Where the Published Post is. A URL is recorded once.</param>
/// <param name="AffiliateLinkId">The affiliate link it carries, when it carries one.</param>
public sealed record PublishedPostRequest(
    Guid RenderedVideoId,
    Guid SocialAccountId,
    DateOnly PublishedOn,
    [Required, StringLength(Product.UrlMaxLength), WebAddress] string Url,
    Guid? AffiliateLinkId = null);

/// <summary>A Published Post with the Rendered Video it shows and what that was made from, as those are now.</summary>
/// <param name="AffiliateLink">Null when it carries none.</param>
/// <param name="AffiliateLinkShared">
/// Whether another Published Post carries the same affiliate link. Commission can then be shown
/// for the link or the Product, never for this Published Post.
/// </param>
public sealed record PublishedPostResponse(
    Guid Id,
    Guid RenderedVideoId,
    Guid ProductId,
    string ProductName,
    Guid ProjectId,
    Guid VariantId,
    CreativeTemplate CreativeTemplate,
    string Hook,
    SocialAccountResponse SocialAccount,
    DateOnly PublishedOn,
    string Url,
    PublishedPostLinkResponse? AffiliateLink,
    bool AffiliateLinkShared,
    DateTimeOffset CreatedAt);

/// <summary>The affiliate link a Published Post carries.</summary>
public sealed record PublishedPostLinkResponse(Guid Id, string Url, string Label);
