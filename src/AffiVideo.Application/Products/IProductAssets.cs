using AffiVideo.Domain;

namespace AffiVideo.Application.Products;

/// <summary>
/// The photos and logos of the caller's Organization's Products. A Product or an
/// asset of another Organization is answered exactly as one that does not exist.
/// </summary>
public interface IProductAssets
{
    /// <summary>
    /// Decodes what was sent, and keeps it as a PNG if it is an image within the
    /// limits. A new logo takes the place of the Product's logo.
    /// </summary>
    /// <param name="upload">The bytes a member sent. Nothing else about the file is trusted or asked for.</param>
    /// <returns>Null when there is no such Product.</returns>
    Task<AssetUpload?> AddAsync(Guid productId, ProductAssetKind kind, Stream upload, CancellationToken cancellationToken);

    /// <summary>Oldest first. Null when there is no such Product.</summary>
    Task<IReadOnlyList<ProductAsset>?> ListAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>The stored PNG, for the caller to dispose. Null when the Product has no such asset.</summary>
    Task<AssetContent?> OpenAsync(Guid productId, Guid assetId, CancellationToken cancellationToken);

    /// <returns>False when the Product has no such asset.</returns>
    Task<bool> RemoveAsync(Guid productId, Guid assetId, CancellationToken cancellationToken);
}

/// <summary>Either the asset that was kept, or the reason the upload was refused, in words for the member.</summary>
public sealed record AssetUpload(ProductAsset? Asset, string? Refused)
{
    public static AssetUpload Kept(ProductAsset asset) => new(asset, null);

    public static AssetUpload Refuse(string reason) => new(null, reason);
}

public sealed record AssetContent(ProductAsset Asset, Stream Content);
