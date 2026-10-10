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
/// <param name="CurrentPerformance">Its latest Performance Snapshot, which is its current figure. Null when it has none.</param>
/// <param name="Commission">
/// What is recorded for its affiliate link, one total for each currency: empty when nothing is recorded yet.
/// Null when it carries no affiliate link, or one that another Published Post carries too. Commission is then
/// not known for this Published Post, and is never worked out from views or clicks.
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
    CurrentPerformanceResponse? CurrentPerformance,
    IReadOnlyList<CommissionTotalResponse>? Commission,
    DateTimeOffset CreatedAt);

/// <summary>The affiliate link a Published Post carries.</summary>
public sealed record PublishedPostLinkResponse(Guid Id, string Url, string Label);

/// <summary>
/// The running totals of a Published Post as a member read them off the platform.
/// Each is the total so far, not what was gained since the last snapshot. One that
/// is left out is unknown, which is not zero. At least one is given.
/// </summary>
/// <param name="TakenAt">The moment the totals apply to: when they were read. Not in the future.</param>
public sealed record PerformanceSnapshotRequest(
    DateTimeOffset TakenAt,
    [RunningTotal] long? Views = null,
    [RunningTotal] long? Likes = null,
    [RunningTotal] long? Comments = null,
    [RunningTotal] long? Shares = null,
    [RunningTotal] long? Clicks = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Views is null && Likes is null && Comments is null && Shares is null && Clicks is null)
        {
            yield return new ValidationResult("Enter at least one figure.", [nameof(Views)]);
        }
    }
}

/// <summary>A Performance Snapshot. A total that is null is unknown, not zero.</summary>
/// <param name="TakenAt">The moment the totals apply to.</param>
/// <param name="Source">Where the figures came from.</param>
/// <param name="RecordedAt">When the snapshot was entered.</param>
/// <param name="LowerThanPrevious">
/// The totals that are lower than in the snapshot before this one. A running total does not
/// usually go down, so each is worth checking. Empty when none is.
/// </param>
public sealed record PerformanceSnapshotResponse(
    Guid Id,
    Guid PublishedPostId,
    DateTimeOffset TakenAt,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Clicks,
    PerformanceSource Source,
    DateTimeOffset RecordedAt,
    IReadOnlyList<PerformanceMetric> LowerThanPrevious);

/// <summary>The current figure of a Published Post: its latest Performance Snapshot. A total that is null is unknown, not zero.</summary>
/// <param name="TakenAt">The moment the totals apply to.</param>
/// <param name="Source">Where the figures came from.</param>
/// <param name="RecordedAt">When the snapshot was entered.</param>
public sealed record CurrentPerformanceResponse(
    Guid SnapshotId,
    DateTimeOffset TakenAt,
    long? Views,
    long? Likes,
    long? Comments,
    long? Shares,
    long? Clicks,
    PerformanceSource Source,
    DateTimeOffset RecordedAt);

/// <summary>
/// What an affiliate report says for a period, attached to the affiliate link or the Product
/// the report gives the figures for: exactly one of the two.
/// </summary>
/// <param name="PeriodStart">The first day the figures cover.</param>
/// <param name="PeriodEnd">The last day the figures cover. Not before the first.</param>
/// <param name="Source">The report the figures were read from, such as the name of the affiliate programme.</param>
/// <param name="Currency">The currency every amount is in.</param>
/// <param name="Commission">What was earned, before anything was taken back.</param>
/// <param name="Orders">Left out when the report does not say, which is unknown and not zero.</param>
/// <param name="ConfirmedOrders">Left out when the report does not say, which is unknown and not zero.</param>
/// <param name="Refunds">Commission taken back because orders were refunded. It reduces the net figure.</param>
/// <param name="Adjustments">Commission taken back for any other reason. It reduces the net figure.</param>
public sealed record CommissionRecordRequest(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    [Required, StringLength(CommissionRecord.SourceMaxLength)] string Source,
    [Required, CurrencyCode] string Currency,
    [Price] decimal Commission,
    Guid? AffiliateLinkId = null,
    Guid? ProductId = null,
    [Range(0, int.MaxValue)] int? Orders = null,
    [Range(0, int.MaxValue)] int? ConfirmedOrders = null,
    [Price] decimal Refunds = 0,
    [Price] decimal Adjustments = 0) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AffiliateLinkId is null == ProductId is null)
        {
            yield return new ValidationResult(
                "Attach the record to one affiliate link or to one Product, not both.", [nameof(AffiliateLinkId)]);
        }
        if (PeriodEnd < PeriodStart)
        {
            yield return new ValidationResult("The period cannot end before it starts.", [nameof(PeriodEnd)]);
        }
    }
}

/// <summary>A Commission record. It is attached to an affiliate link or to a Product, never both.</summary>
/// <param name="Orders">Null when the report did not say, which is unknown and not zero.</param>
/// <param name="ConfirmedOrders">Null when the report did not say, which is unknown and not zero.</param>
/// <param name="Net">The Commission less the refunds and the adjustments.</param>
/// <param name="RecordedAt">When the record was entered.</param>
public sealed record CommissionRecordResponse(
    Guid Id,
    Guid? AffiliateLinkId,
    Guid? ProductId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Source,
    string Currency,
    int? Orders,
    int? ConfirmedOrders,
    decimal Commission,
    decimal Refunds,
    decimal Adjustments,
    decimal Net,
    DateTimeOffset RecordedAt);

/// <summary>Commission records in one currency, added up. Amounts in different currencies are never added to each other.</summary>
/// <param name="Orders">Null unless every record says how many, which is unknown and not zero.</param>
/// <param name="ConfirmedOrders">Null unless every record says how many.</param>
/// <param name="Net">The Commission less the refunds and the adjustments.</param>
/// <param name="Records">How many records were added up.</param>
/// <param name="Sources">The reports the figures were read from.</param>
/// <param name="PeriodStart">The first day any of the records covers.</param>
/// <param name="PeriodEnd">The last day any of the records covers.</param>
public sealed record CommissionTotalResponse(
    string Currency,
    int? Orders,
    int? ConfirmedOrders,
    decimal Commission,
    decimal Refunds,
    decimal Adjustments,
    decimal Net,
    int Records,
    IReadOnlyList<string> Sources,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

/// <summary>What is recorded for an affiliate link.</summary>
/// <param name="Label">Empty when the link has no name of its own.</param>
/// <param name="PublishedPostCount">
/// How many Published Posts carry the link. Above one, the totals belong to all of them together
/// and cannot be split by post.
/// </param>
public sealed record LinkCommissionResponse(
    Guid AffiliateLinkId, string Url, string Label, int PublishedPostCount, IReadOnlyList<CommissionTotalResponse> Totals);

/// <summary>
/// What is recorded for a Product, and for every affiliate link that only Published Posts of
/// that Product carry.
/// </summary>
public sealed record ProductCommissionResponse(Guid ProductId, string ProductName, IReadOnlyList<CommissionTotalResponse> Totals);

/// <summary>A count so far: a whole number of zero or more.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class RunningTotalAttribute() : ValidationAttribute("Enter a whole number of zero or more.")
{
    public override bool IsValid(object? value) => value is null or long and >= 0;
}
