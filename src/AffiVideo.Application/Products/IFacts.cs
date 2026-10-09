using AffiVideo.Domain;

namespace AffiVideo.Application.Products;

/// <summary>
/// The Facts of the caller's Organization's Products. A Product or a Fact of
/// another Organization is answered exactly as one that does not exist.
/// </summary>
public interface IFacts
{
    /// <summary>Adds a Proposed Fact.</summary>
    /// <returns>Null when there is no such Product.</returns>
    Task<FactRecord?> AddAsync(Guid productId, FactDetails details, CancellationToken cancellationToken);

    /// <summary>Oldest first, in every state unless one is asked for. Null when there is no such Product.</summary>
    Task<Page<FactRecord>?> ListAsync(Guid productId, FactState? state, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Null when the Product has no such Fact.</summary>
    Task<FactRecord?> FindAsync(Guid productId, Guid factId, CancellationToken cancellationToken);

    /// <summary>Confirms a Proposed Fact in the calling member's name, and records it in the audit log.</summary>
    /// <returns>Null when the Product has no such Fact.</returns>
    Task<FactChange?> ConfirmAsync(Guid productId, Guid factId, CancellationToken cancellationToken);

    /// <summary>Withdraws a Proposed or Confirmed Fact, and records it in the audit log.</summary>
    /// <returns>Null when the Product has no such Fact.</returns>
    Task<FactChange?> WithdrawAsync(Guid productId, Guid factId, CancellationToken cancellationToken);

    /// <summary>
    /// What editing a Fact is: the Fact is Withdrawn and a new Proposed one is added
    /// to the same Product, both or neither.
    /// </summary>
    /// <returns>The new Fact. Null when the Product has no such Fact.</returns>
    Task<FactChange?> ReplaceAsync(Guid productId, Guid factId, FactDetails details, CancellationToken cancellationToken);
}

/// <param name="ConfirmedByEmail">Of the member who Confirmed it, when one did.</param>
public sealed record FactRecord(Fact Fact, string? ConfirmedByEmail);

/// <summary>Either the Fact as it now is, or the reason its state could not change, in words for the member.</summary>
public sealed record FactChange(FactRecord? Record, string? Refused)
{
    public static FactChange Made(FactRecord record) => new(record, null);

    public static FactChange Refuse(string reason) => new(null, reason);
}

/// <summary>The Facts the seed command gives the demonstration Product, already Confirmed by the demonstration Owner.</summary>
public static class DemonstrationFacts
{
    // Fixed, so that seeding again finds each one even after a member has withdrawn it.
    public static IReadOnlyList<(Guid Id, FactDetails Details)> All { get; } =
    [
        (new("0199c0de-a1b2-7000-8000-0000000fac01"),
            new FactDetails("Chống ồn chủ động, giảm tiếng ồn xung quanh khi di chuyển", ContentLanguages.Vietnamese, Source)),
        (new("0199c0de-a1b2-7000-8000-0000000fac02"),
            new FactDetails("Pin nghe nhạc liên tục 30 giờ khi dùng kèm hộp sạc", ContentLanguages.Vietnamese, Source)),
        (new("0199c0de-a1b2-7000-8000-0000000fac03"),
            new FactDetails("Kết nối Bluetooth 5.3, ghép đôi nhanh với điện thoại", ContentLanguages.Vietnamese, Source)),
    ];

    private const string Source = "Dữ liệu mẫu cho sản phẩm hư cấu";
}
