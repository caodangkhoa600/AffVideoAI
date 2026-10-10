namespace AffiVideo.Domain;

/// <summary>
/// What an affiliate report says for a period: orders, confirmed orders,
/// Commission, and what was taken back from it or added to it. It is attached
/// to exactly one affiliate link or one Product, which is the level the report
/// gives the figures at. Nothing about it changes afterwards: a wrong one is
/// deleted and recorded again.
/// </summary>
public sealed class CommissionRecord : IOwnedByOrganization
{
    public const int ReportMaxLength = 100;

    // For the data-access layer, which fills the properties itself.
    private CommissionRecord()
    {
    }

    /// <param name="target">The affiliate link or the Product the report gives the figures for.</param>
    /// <param name="report">The report the figures were read from, as the member names it.</param>
    public CommissionRecord(
        Guid id, Guid organizationId, CommissionTarget target, DateOnly periodStart, DateOnly periodEnd, string report,
        string currency, CommissionFigures figures, CommissionSource source, DateTimeOffset recordedAt)
    {
        if (target.AffiliateLinkId is null == target.ProductId is null)
        {
            throw new ArgumentException("A Commission record is attached to one affiliate link or one Product, never both.", nameof(target));
        }
        if (periodEnd < periodStart)
        {
            throw new ArgumentException("The period of a Commission record does not end before it starts.", nameof(periodEnd));
        }

        Id = id;
        OrganizationId = organizationId;
        AffiliateLinkId = target.AffiliateLinkId;
        ProductId = target.ProductId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Report = report.Trim();
        Currency = currency;
        Orders = figures.Orders;
        ConfirmedOrders = figures.ConfirmedOrders;
        Commission = figures.Commission;
        Refunds = figures.Refunds;
        Adjustments = figures.Adjustments;
        Source = source;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The affiliate link the figures are for. Absent when they are for a Product.</summary>
    public Guid? AffiliateLinkId { get; private set; }

    /// <summary>The Product the figures are for. Absent when they are for an affiliate link.</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>The first day the figures cover.</summary>
    public DateOnly PeriodStart { get; private set; }

    /// <summary>The last day the figures cover.</summary>
    public DateOnly PeriodEnd { get; private set; }

    /// <summary>The report the figures were read from, as the member names it.</summary>
    public string Report { get; private set; } = "";

    /// <summary>The three-letter code of the currency every amount is in.</summary>
    public string Currency { get; private set; } = "";

    /// <summary>Null when the report does not say, which is unknown and not zero.</summary>
    public int? Orders { get; private set; }

    /// <summary>Null when the report does not say, which is unknown and not zero.</summary>
    public int? ConfirmedOrders { get; private set; }

    /// <summary>What was earned, before anything was taken back or added.</summary>
    public decimal Commission { get; private set; }

    /// <summary>Commission taken back because orders were refunded. Zero or more.</summary>
    public decimal Refunds { get; private set; }

    /// <summary>What the programme changed for any other reason: below zero when it took Commission away, above when it added some.</summary>
    public decimal Adjustments { get; private set; }

    /// <summary>What is left of the Commission: less the refunds, with the adjustments.</summary>
    public decimal Net => Commission - Refunds + Adjustments;

    /// <summary>How the figures got in.</summary>
    public CommissionSource Source { get; private set; }

    /// <summary>When the record was entered.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>
    /// Whether the two are for the same affiliate link or Product, in the same currency,
    /// and cover some of the same days. Their Commission may then be counted twice.
    /// </summary>
    public bool Overlaps(CommissionRecord other) =>
        other.Id != Id
        && other.AffiliateLinkId == AffiliateLinkId
        && other.ProductId == ProductId
        && other.Currency == Currency
        && other.PeriodStart <= PeriodEnd
        && other.PeriodEnd >= PeriodStart;
}

/// <summary>How the figures of a Commission record got in.</summary>
public enum CommissionSource
{
    /// <summary>A member typed them in.</summary>
    Manual,
}

/// <summary>What a Commission record is attached to: one affiliate link or one Product.</summary>
public sealed record CommissionTarget(Guid? AffiliateLinkId, Guid? ProductId);

/// <summary>The figures of one report for one period. An order count that is null is unknown.</summary>
/// <param name="Refunds">Commission taken back because orders were refunded. Zero or more.</param>
/// <param name="Adjustments">Below zero when the programme took Commission away for another reason, above when it added some.</param>
public sealed record CommissionFigures(int? Orders, int? ConfirmedOrders, decimal Commission, decimal Refunds, decimal Adjustments);

/// <summary>Commission records in one currency, added up.</summary>
/// <param name="Orders">Null unless every record says how many, which is unknown and not zero.</param>
/// <param name="ConfirmedOrders">Null unless every record says how many.</param>
/// <param name="Net">The Commission less the refunds, with the adjustments.</param>
/// <param name="Records">How many records were added up.</param>
/// <param name="OverlappingRecords">
/// How many of them cover some of the same days as another of them for the same affiliate link or
/// Product. Above zero, part of the total may be counted twice.
/// </param>
/// <param name="Reports">The reports the figures were read from, by name.</param>
/// <param name="Sources">How the figures got in.</param>
/// <param name="PeriodStart">The first day any of the records covers.</param>
/// <param name="PeriodEnd">The last day any of the records covers.</param>
public sealed record CommissionTotal(
    string Currency,
    int? Orders,
    int? ConfirmedOrders,
    decimal Commission,
    decimal Refunds,
    decimal Adjustments,
    decimal Net,
    int Records,
    int OverlappingRecords,
    IReadOnlyList<string> Reports,
    IReadOnlyList<CommissionSource> Sources,
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

public static class Commissions
{
    public const int AmountPrecision = Product.PricePrecision;

    public const int AmountDecimals = Product.PriceDecimals;

    /// <summary>
    /// The records added up, one total for each currency they are in, by currency
    /// code. Amounts in different currencies are never added to each other, and no
    /// records make no total. Nothing here divides a figure: a total says as much
    /// as its records say, at the level they were recorded at.
    /// </summary>
    public static IReadOnlyList<CommissionTotal> Totals(IEnumerable<CommissionRecord> records) =>
        [.. records
            .GroupBy(record => record.Currency)
            .OrderBy(currency => currency.Key, StringComparer.Ordinal)
            .Select(currency => new CommissionTotal(
                currency.Key,
                currency.All(record => record.Orders is not null) ? currency.Sum(record => record.Orders) : null,
                currency.All(record => record.ConfirmedOrders is not null) ? currency.Sum(record => record.ConfirmedOrders) : null,
                currency.Sum(record => record.Commission),
                currency.Sum(record => record.Refunds),
                currency.Sum(record => record.Adjustments),
                currency.Sum(record => record.Net),
                currency.Count(),
                currency.Count(record => currency.Any(record.Overlaps)),
                [.. currency.Select(record => record.Report).Distinct().Order(StringComparer.Ordinal)],
                [.. currency.Select(record => record.Source).Distinct().Order()],
                currency.Min(record => record.PeriodStart),
                currency.Max(record => record.PeriodEnd)))];
}
