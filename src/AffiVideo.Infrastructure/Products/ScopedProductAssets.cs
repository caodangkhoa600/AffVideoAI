using AffiVideo.Application;
using AffiVideo.Application.Products;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Images;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AffiVideo.Infrastructure.Products;

// No method names an Organization: the context's filter leaves only the caller's
// Products and assets, and a file is only ever looked up by the key of an asset found that way.
internal sealed class ScopedProductAssets(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    ILogger<ScopedProductAssets> logger) : IProductAssets
{
    public async Task<AssetUpload?> AddAsync(Guid productId, ProductAssetKind kind, Stream upload, CancellationToken cancellationToken)
    {
        if (!await ProductExistsAsync(productId, cancellationToken)) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("An asset is stored for the caller's Organization, and there is no caller.");

        var image = await ImageNormaliser.NormaliseAsync(upload, cancellationToken);
        if (image.Png is null) return AssetUpload.Refuse(image.Refused!);

        var replaced = new List<ProductAsset>();
        if (kind == ProductAssetKind.Logo)
        {
            replaced = await database.ProductAssets
                .Where(a => a.ProductId == productId && a.Kind == ProductAssetKind.Logo)
                .ToListAsync(cancellationToken);
            database.ProductAssets.RemoveRange(replaced);
        }
        else if (await database.ProductAssets.CountAsync(a => a.ProductId == productId && a.Kind == kind, cancellationToken) >= ProductAsset.MaxPhotos)
        {
            return AssetUpload.Refuse($"A Product can have at most {ProductAsset.MaxPhotos} photos. Remove one first.");
        }

        var asset = new ProductAsset(
            Guid.CreateVersion7(), organizationId, productId, kind, image.Width, image.Height, image.Png.Length, clock.GetUtcNow());
        database.ProductAssets.Add(asset);

        // The file first: a record never points at a file that is not there.
        using (var png = new MemoryStream(image.Png, writable: false))
        {
            await storage.PutAsync(asset.StorageKey, png, ProductAsset.ContentType, cancellationToken);
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await DeleteFileAsync(asset);
            if (exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            {
                return AssetUpload.Refuse("Someone else changed this Product's logo at the same moment. Try again.");
            }
            throw;
        }

        foreach (var old in replaced) await DeleteFileAsync(old);
        return AssetUpload.Kept(asset);
    }

    public async Task<IReadOnlyList<ProductAsset>?> ListAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (!await ProductExistsAsync(productId, cancellationToken)) return null;

        return await database.ProductAssets.AsNoTracking()
            .Where(a => a.ProductId == productId)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AssetContent?> OpenAsync(Guid productId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await FindAsync(productId, assetId, cancellationToken);
        if (asset is null) return null;

        var content = await storage.OpenAsync(asset.StorageKey, cancellationToken);
        if (content is null)
        {
            logger.LogError("The file of asset {AssetId} is missing from storage at {StorageKey}", asset.Id, asset.StorageKey);
            return null;
        }
        return new AssetContent(asset, content);
    }

    public async Task<bool> RemoveAsync(Guid productId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await FindAsync(productId, assetId, cancellationToken);
        if (asset is null) return false;

        database.ProductAssets.Remove(asset);
        await database.SaveChangesAsync(cancellationToken);
        await DeleteFileAsync(asset);
        return true;
    }

    private Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken) =>
        database.Products.AnyAsync(p => p.Id == productId, cancellationToken);

    // The asset has to be this Product's: an identifier from one Product does not work under another.
    private Task<ProductAsset?> FindAsync(Guid productId, Guid assetId, CancellationToken cancellationToken) =>
        database.ProductAssets.SingleOrDefaultAsync(a => a.Id == assetId && a.ProductId == productId, cancellationToken);

    // The record is what makes a file reachable, and it is already gone or was never
    // saved. A file left behind is wasted space, not a reason to fail the request, and
    // it is deleted even if the member has stopped waiting.
    private async Task DeleteFileAsync(ProductAsset asset)
    {
        // With it go the files a render made from it, if any did.
        foreach (var key in new[] { asset.StorageKey, asset.VideoLayerKey, asset.VideoLayerDetailsKey })
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", key);
            }
        }
    }
}
