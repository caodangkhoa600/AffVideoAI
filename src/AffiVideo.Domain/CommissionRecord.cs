namespace AffiVideo.Domain;

/// <summary>
/// What an affiliate report says for a period: orders, confirmed orders,
/// Commission, and what was taken back from it. It is attached to exactly one
/// affiliate link or one Product, which is the level the report gives the
/// figures at. Nothing about it changes afterwards: a wrong one is deleted and
/// recorded again.
/// </summary>
public sealed class CommissionRecord : IOwnedByOrganization
{
    public const int SourceMaxLength = 100;

    // For the data-access layer, which fills the properties itself.
    private CommissionRecord()
    {
    }

    /// <param name="target">The affiliate link or the Product the report gives the figures for.</param>
    public CommissionRecord(
        Guid id, Guid organizationId, CommissionTarget target, DateOnly periodStart, DateOnly periodEnd, string source,
        string currency, CommissionFigures figures, DateTimeOffset recordedAt)
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
        Source = source.Trim();
        Currency = currency;
        Orders = figures.Orders;
        ConfirmedOrders = figures.ConfirmedOrders;
        Commission = figures.Commission;
        Refunds = figures.Refunds;
        Adjustments = figures.Adjustments;
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
    public string Source { get; private set; } = "";

    /// <summary>The three-letter code of the currency every amount is in.</summary>
    public string Currency { get; private set; } = "";

    /// <summary>Null when the report does not say, which is unknown and not zero.</summary>
    public int? Orders { get; private set; }

    /// <summary>Null when the report does not say, which is unknown and not zero.</summary>
    public int? ConfirmedOrders { get; private set; }

    /// <summary>What was earned, before anything was taken back.</summary>
    public decimal Commission { get; private set; }

    /// <summary>Commission taken back because orders were refunded.</summary>
    public decimal Refunds { get; private set; }

    /// <summary>Commission taken back for any other reason.</summary>
    public decimal Adjustments { get; private set; }

    /// <summary>What is left of the Commission.</summary>
    public decimal Net => Commission - Refunds - Adjustments;

    /// <summary>When the record was entered.</summary>
    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>What a Commission record is attached to: one affiliate link or one Product.</summary>
public sealed record CommissionTarget(Guid? AffiliateLinkId, Guid? ProductId);

/// <summary>The figures of one report for one period. An order count that is null is unknown.</summary>
/// <param name="Refunds">Commission taken back because orders were refunded.</param>
/// <param name="Adjustments">Commission taken back for any other reason.</param>
public sealed record CommissionFigures(int? Orders, int? ConfirmedOrders, decimal Commission, decimal Refunds, decimal Adjustments);

/// <summary>Commission records in one currency, added up.</summary>
/// <param name="Orders">Null unless every record says how many, which is unknown and not zero.</param>
/// <param name="ConfirmedOrders">Null unless every record says how many.</param>
/// <param name="Net">The Commission less the refunds and the adjustments.</param>
/// <param name="Records">How many records were added up.</param>
/// <param name="Sources">The reports the figures were read from, by name.</param>
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
    IReadOnlyList<string> Sources,
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
                [.. currency.Select(record => record.Source).Distinct().Order(StringComparer.Ordinal)],
                currency.Min(record => record.PeriodStart),
                currency.Max(record => record.PeriodEnd)))];
}
