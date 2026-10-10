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
    Task<CommissionRecordOutcome> RecordAsync(NewCommissionRecord record, CancellationToken cancellationToken);

    /// <summary>The latest period first.</summary>
    Task<Page<CommissionRecord>> ListAsync(CommissionTarget filter, PageRequest page, CancellationToken cancellationToken);

    /// <returns>False when there is no such record.</returns>
    Task<bool> DeleteAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>What is recorded for an affiliate link, with how many Published Posts carry it.</summary>
    /// <returns>Null when there is no such affiliate link.</returns>
    Task<LinkCommission?> ForLinkAsync(Guid linkId, CancellationToken cancellationToken);

    /// <summary>
    /// What is recorded for a Product, and for every affiliate link that only
    /// Published Posts of that Product carry.
    /// </summary>
    /// <returns>Null when there is no such Product.</returns>
    Task<ProductCommission?> ForProductAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed record NewCommissionRecord(
    CommissionTarget Target, DateOnly PeriodStart, DateOnly PeriodEnd, string Source, string Currency, CommissionFigures Figures);

/// <param name="PublishedPostCount">How many Published Posts carry the link. Above one, the totals cannot be split by post.</param>
public sealed record LinkCommission(AffiliateLink Link, int PublishedPostCount, IReadOnlyList<CommissionTotal> Totals);

/// <param name="ProductName">As the Product is named now.</param>
public sealed record ProductCommission(Guid ProductId, string ProductName, IReadOnlyList<CommissionTotal> Totals);

/// <summary>
/// The record as kept; or what in the request names nothing of the Organization,
/// by field; or the reason it was refused, in words for the member.
/// </summary>
public sealed record CommissionRecordOutcome(CommissionRecord? Record, IReadOnlyDictionary<string, string>? Unknown, string? Refused)
{
    public static CommissionRecordOutcome Recorded(CommissionRecord record) => new(record, null, null);

    public static CommissionRecordOutcome NoSuch(string field, string reason) =>
        new(null, new Dictionary<string, string> { [field] = reason }, null);

    public static CommissionRecordOutcome Refuse(string reason) => new(null, null, reason);
}
