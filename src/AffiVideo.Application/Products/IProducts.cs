using AffiVideo.Domain;

namespace AffiVideo.Application.Products;

/// <summary>
/// The Products of the caller's Organization. A Product of another Organization
/// is answered with null, exactly as one that does not exist.
/// </summary>
public interface IProducts
{
    Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken);

    Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken);

    Task<Product?> ChangeAsync(Guid productId, ProductDetails details, CancellationToken cancellationToken);

    Task<Product?> ArchiveAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>By name.</summary>
    Task<Page<Product>> ListAsync(ProductFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Every category the Organization's Products use, in alphabetical order.</summary>
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken);
}

/// <summary>Each part narrows the list; an absent part does not.</summary>
/// <param name="Search">Text the name contains, in any letter case.</param>
/// <param name="Category">The exact category.</param>
public sealed record ProductFilter(string? Search, string? Category, ProductStatus? Status);

/// <summary>The fictional Product the seed command adds to the demonstration Organization.</summary>
public static class DemonstrationProduct
{
    // Fixed, so that seeding again finds it even after a member has renamed it.
    public static readonly Guid Id = new("0199c0de-a1b2-7000-8000-000000a1bea7");

    public static ProductDetails Details { get; } = new(
        Name: "AirBeat X1",
        Category: "Tai nghe",
        Brand: "AirBeat",
        Description:
            "Tai nghe không dây AirBeat X1 (sản phẩm hư cấu, dùng để thử hệ thống): chống ồn chủ động, " +
            "hộp sạc nhỏ gọn, kết nối Bluetooth.",
        Price: 1_290_000m,
        Currency: "VND",
        OriginalUrl: "https://shop.example/airbeat-x1",
        AffiliateUrl: null,
        TargetAudience: "Người đi làm và sinh viên nghe nhạc khi di chuyển",
        Tags: ["tai nghe", "bluetooth", "chống ồn"]);
}
