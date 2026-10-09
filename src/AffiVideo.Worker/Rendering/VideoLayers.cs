using System.Text.Json;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// The Product photos as a video shows them. A photo is cut out and given its
/// shadow once, by the first render that needs it, and the result is kept in
/// object storage beside the photo for every render after.
/// </summary>
internal sealed class VideoLayers(IObjectStorage storage, IImageProcessor cutOut, ILogger<VideoLayers> logger)
{
    public async Task<VideoLayer> ForAsync(ProductAsset asset, CancellationToken cancellationToken)
    {
        if (await ReadAsync(asset.VideoLayerDetailsKey, cancellationToken) is { } kept
            && JsonSerializer.Deserialize<VideoLayerDetails>(kept, JsonSerializerOptions.Web) is { Version: VideoLayerDetails.Current } details
            && await ReadAsync(asset.VideoLayerKey, cancellationToken) is { } image)
        {
            return new VideoLayer(image, details);
        }

        var file = await ReadAsync(asset.StorageKey, cancellationToken)
            ?? throw new RenderFailedException(
                "A photo the Storyboard shows has since been removed from the Product. Generate the Storyboard again, then render that version.");
        var photo = Pixels.Decode(file);
        var (subject, keptWholeBecause) = await SubjectAsync(photo, cancellationToken);
        if (keptWholeBecause is not null)
        {
            logger.LogInformation("Asset {AssetId} is shown whole, on a card: {Reason}", asset.Id, keptWholeBecause);
        }

        var layer = ProductLayer.Make(subject);
        var (hue, saturation) = layer.Colour();
        var made = new VideoLayer(
            layer.Image.EncodePng(),
            new VideoLayerDetails(
                VideoLayerDetails.Current, layer.Image.Width, layer.Image.Height, layer.ProductWidth, layer.ProductHeight,
                keptWholeBecause is null, hue, saturation, keptWholeBecause));

        // The details last: they are only ever there for an image that is.
        await WriteAsync(asset.VideoLayerKey, made.Png, ProductAsset.ContentType, cancellationToken);
        await WriteAsync(
            asset.VideoLayerDetailsKey, JsonSerializer.SerializeToUtf8Bytes(made.Details, JsonSerializerOptions.Web),
            "application/json", cancellationToken);
        return made;
    }

    // The Product cut out of the photo, or the whole photo as a card when no cut-out passes its checks.
    private async Task<(Pixels Subject, string? KeptWholeBecause)> SubjectAsync(Pixels photo, CancellationToken cancellationToken)
    {
        // A photo that came already cut out is checked like any other cut-out, and cut again if it fails.
        if (CutOut.HasOwnTransparency(photo) && CutOut.Problems(photo, byModel: false).Count == 0) return (photo, null);

        var whole = CutOut.Uncut(photo);
        using var asTaken = new MemoryStream(whole.EncodePng(), writable: false);
        await using var answer = await cutOut.CutOutAsync(asTaken, cancellationToken);
        using var read = new MemoryStream();
        await answer.CopyToAsync(read, cancellationToken);
        var cut = Pixels.Decode(read.ToArray());

        var problems = cut.Width == photo.Width && cut.Height == photo.Height && CutOut.ShowsOnlyThePhoto(cut = CutOut.HidingWhatThePhotoHid(cut, photo), whole)
            ? CutOut.Problems(cut, byModel: true)
            : ["the cut-out changed the Product's own pixels"];
        return problems.Count == 0 ? (cut, null) : (CutOut.AsCard(photo), string.Join("; ", problems));
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        await using var content = await storage.OpenAsync(key, cancellationToken);
        if (content is null) return null;
        using var read = new MemoryStream();
        await content.CopyToAsync(read, cancellationToken);
        return read.ToArray();
    }

    private async Task WriteAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        await storage.PutAsync(key, stream, contentType, cancellationToken);
    }
}

internal sealed record VideoLayer(byte[] Png, VideoLayerDetails Details);

/// <summary>What is kept about a photo's video layer, beside it in object storage.</summary>
/// <param name="Version">Of how layers are made. A layer made another way is made again.</param>
/// <param name="ProductWidth">Of the Product within the image, in pixels. It is centred.</param>
/// <param name="CutOut">False when the photo is shown whole, on a card.</param>
/// <param name="Hue">Of the Product's most characteristic colour, from 0 to 1.</param>
/// <param name="Saturation">Of that colour. 0 for a Product with no colour to speak of.</param>
/// <param name="KeptWholeBecause">Why the Product was not cut out, when it was not.</param>
internal sealed record VideoLayerDetails(
    int Version, int Width, int Height, int ProductWidth, int ProductHeight,
    bool CutOut, double Hue, double Saturation, string? KeptWholeBecause)
{
    public const int Current = 1;
}
