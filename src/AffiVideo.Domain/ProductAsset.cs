namespace AffiVideo.Domain;

/// <summary>
/// A photo or the logo of a Product. What is kept is never the file a member sent
/// but a PNG made by decoding it, so every asset is the same kind of file.
/// </summary>
public sealed class ProductAsset : IOwnedByOrganization
{
    /// <summary>The largest file a member may send.</summary>
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    /// <summary>The most pixels an image may have along either side.</summary>
    public const int MaxDimension = 6000;

    /// <summary>The most photos one Product may have. It has at most one logo.</summary>
    public const int MaxPhotos = 30;

    /// <summary>The fewest pixels along either side of a photo that a video can show without it looking soft.</summary>
    public const int MinVideoPhotoSide = 400;

    /// <summary>What every stored asset is.</summary>
    public const string ContentType = "image/png";

    // For the data-access layer, which fills the properties itself.
    private ProductAsset()
    {
    }

    public ProductAsset(
        Guid id, Guid organizationId, Guid productId, ProductAssetKind kind,
        int width, int height, long sizeInBytes, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProductId = productId;
        Kind = kind;
        Width = width;
        Height = height;
        SizeInBytes = sizeInBytes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ProductId { get; private set; }

    public ProductAssetKind Kind { get; private set; }

    /// <summary>In pixels, of the stored PNG: a photo taken sideways has been turned upright.</summary>
    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Of the stored PNG, not of the file that was sent.</summary>
    public long SizeInBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether a video can show the Product from this: a photo, not the logo, and large enough.</summary>
    public bool IsUsableInVideo => Kind == ProductAssetKind.Photo && Math.Min(Width, Height) >= MinVideoPhotoSide;

    /// <summary>
    /// Where the file is in object storage. It starts with the Organization, so
    /// everything one Organization has stored is under one prefix of its own.
    /// </summary>
    public string StorageKey => $"organizations/{OrganizationId}/products/{ProductId}/assets/{Id}.png";

    /// <summary>
    /// Where the photo is kept as a video shows it: the Product cut out and standing
    /// on its shadow. Made by the first render that needs it, and removed with the asset.
    /// </summary>
    public string VideoLayerKey => $"organizations/{OrganizationId}/products/{ProductId}/assets/{Id}.video-layer.png";

    /// <summary>What is known about <see cref="VideoLayerKey"/>: its measurements and how it was made.</summary>
    public string VideoLayerDetailsKey => $"organizations/{OrganizationId}/products/{ProductId}/assets/{Id}.video-layer.json";
}

public enum ProductAssetKind
{
    Photo,
    Logo,
}
