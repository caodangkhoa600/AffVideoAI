using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>
/// The Commission records of the caller's Organization, and what they add up to
/// at the level they were recorded at. An affiliate link or a Product of another
/// Organization is answered exactly as one that does not exist. Nothing here
/// divides Commission between Published Posts.
/// </summary>
public interface ICommissionRecords
{
    /// <summary>Records figures a member typed in.</summary>
    Task<CommissionRecordOutcome> RecordManualAsync(NewCommissionRecord record, CancellationToken cancellationToken);

    /// <summary>The latest period first.</summary>
    Task<Page<CommissionRecordEntry>> ListAsync(CommissionRecordFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <returns>False when there is no such record.</returns>
    Task<bool> DeleteAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>What is recorded for an affiliate link, with the Published Posts that carry it.</summary>
    /// <returns>Null when there is no such affiliate link.</returns>
    Task<LinkCommission?> ForLinkAsync(Guid linkId, CancellationToken cancellationToken);

    /// <summary>
    /// What is recorded for a Product, and apart from it what is recorded for the
    /// affiliate links that only Published Posts of that Product carry.
    /// </summary>
    /// <returns>Null when there is no such Product.</returns>
    Task<ProductCommission?> ForProductAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record NewCommissionRecord(
    CommissionTarget Target, DateOnly PeriodStart, DateOnly PeriodEnd, string Report, string Currency, CommissionFigures Figures);

/// <summary>What narrows the list of Commission records. Whatever is left out narrows nothing.</summary>
/// <param name="ProductId">Leaves the records attached to the Product itself.</param>
public sealed record CommissionRecordFilter(Guid? AffiliateLinkId, Guid? ProductId);

/// <param name="OverlapsAnother">
/// Whether another record for the same affiliate link or Product, in the same currency, covers some of the same days.
/// </param>
public sealed record CommissionRecordEntry(CommissionRecord Record, bool OverlapsAnother);

/// <param name="PublishedPostCount">How many Published Posts carry the link. Above one, the totals cannot be split between them.</param>
/// <param name="ProductIds">
/// The Products those Published Posts are of. The link counts among a Product's links only when this is that Product alone.
/// </param>
public sealed record LinkCommission(
    AffiliateLink Link, int PublishedPostCount, IReadOnlyList<Guid> ProductIds, IReadOnlyList<CommissionTotal> Totals);

/// <summary>
/// The two are never added to each other: a report for a Product may already hold
/// what a report for one of its links holds, and nothing here can tell.
/// </summary>
/// <param name="ProductName">As the Product is named now.</param>
/// <param name="RecordedForProduct">What the records attached to the Product add up to.</param>
/// <param name="RecordedForLinks">What the records of the affiliate links only its Published Posts carry add up to.</param>
public sealed record ProductCommission(
    Guid ProductId, string ProductName, IReadOnlyList<CommissionTotal> RecordedForProduct, IReadOnlyList<CommissionTotal> RecordedForLinks);

/// <summary>What is recorded for the affiliate link of a Published Post that no other Published Post carries.</summary>
/// <param name="RecordsBeforePublication">
/// How many of the records cover days before the Published Post was published. What those days earned
/// was not earned by it, and a record cannot be split by day.
/// </param>
public sealed record PublishedPostCommission(IReadOnlyList<CommissionTotal> Totals, int RecordsBeforePublication);

/// <summary>
/// The record as kept; or what in the request names nothing of the Organization,
/// by field; or the reason it was refused, in words for the member.
/// </summary>
public sealed record CommissionRecordOutcome(CommissionRecordEntry? Record, IReadOnlyDictionary<string, string>? NoSuch, string? Refused)
{
    public static CommissionRecordOutcome Recorded(CommissionRecordEntry record) => new(record, null, null);

    public static CommissionRecordOutcome Missing(string field, string reason) =>
        new(null, new Dictionary<string, string> { [field] = reason }, null);

    public static CommissionRecordOutcome Refuse(string reason) => new(null, null, reason);
}
