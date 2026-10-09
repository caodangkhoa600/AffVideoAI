using System.Runtime.InteropServices;
using AffiVideo.Domain;
using SkiaSharp;

namespace AffiVideo.Infrastructure.Images;

/// <summary>
/// Turns what a member sent into the PNG that is kept, or says why it cannot be.
/// Only the bytes are looked at. The PNG holds the pixels the image decodes to and
/// nothing else of the file: where a photo was taken and what took it are left behind.
/// </summary>
internal static class ImageNormaliser
{
    private static readonly string LargestFile = $"{ProductAsset.MaxUploadBytes / (1024 * 1024)} MB";

    public static async Task<NormalisedImage> NormaliseAsync(Stream upload, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(upload, cancellationToken);
        if (bytes is null) return NormalisedImage.Refuse($"The file is larger than {LargestFile}.");
        if (bytes.Length == 0) return NormalisedImage.Refuse("The file is empty.");
        return Normalise(bytes);
    }

    // Null when there is more than may be sent. The count is of what arrives, not of what was declared.
    private static async Task<byte[]?> ReadAsync(Stream upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > ProductAsset.MaxUploadBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static NormalisedImage Normalise(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            return NormalisedImage.Refuse("The file is not a JPEG, PNG or WebP image.");
        }

        // Read from the file's header, before any memory is set aside for the pixels.
        var encoded = codec.Info;
        if (encoded.Width > ProductAsset.MaxDimension || encoded.Height > ProductAsset.MaxDimension)
        {
            return NormalisedImage.Refuse(
                $"The image is {encoded.Width} by {encoded.Height} pixels. The most allowed is {ProductAsset.MaxDimension} on each side.");
        }

        // Its own colour space, so no colour is converted, and alpha that is not
        // multiplied in, so no pixel is rounded.
        var info = new SKImageInfo(
            encoded.Width, encoded.Height, SKColorType.Rgba8888,
            encoded.IsOpaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul, encoded.ColorSpace);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) != SKCodecResult.Success)
        {
            return NormalisedImage.Refuse("The image is damaged and cannot be read.");
        }

        using var upright = Upright(decoded, codec.EncodedOrigin);
        using var pixels = upright.PeekPixels();
        using var png = pixels.Encode(SKEncodedImageFormat.Png, 100);
        return png is null
            ? NormalisedImage.Refuse("The image could not be stored as a PNG.")
            : new NormalisedImage(png.ToArray(), upright.Width, upright.Height, null);
    }

    // A camera held sideways stores the picture as the sensor saw it and notes
    // which way is up. The PNG has no such note, so the pixels are moved instead:
    // moved, never redrawn.
    private static SKBitmap Upright(SKBitmap image, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return image;

        int width = image.Width, height = image.Height;
        var turned = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var upright = new SKBitmap(image.Info.WithSize(turned ? height : width, turned ? width : height));
        var from = MemoryMarshal.Cast<byte, uint>(image.GetPixelSpan());
        var to = MemoryMarshal.Cast<byte, uint>(upright.GetPixelSpan());

        for (var y = 0; y < upright.Height; y++)
        {
            for (var x = 0; x < upright.Width; x++)
            {
                var (fromX, fromY) = origin switch
                {
                    SKEncodedOrigin.TopRight => (width - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (width - 1 - x, height - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, height - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, height - 1 - x),
                    SKEncodedOrigin.RightBottom => (width - 1 - y, height - 1 - x),
                    SKEncodedOrigin.LeftBottom => (width - 1 - y, x),
                    _ => (x, y),
                };
                to[y * upright.Width + x] = from[fromY * width + fromX];
            }
        }
        return upright;
    }
}

/// <summary>Either the PNG to keep, or the reason there is none, in words for the member.</summary>
internal sealed record NormalisedImage(byte[]? Png, int Width, int Height, string? Refused)
{
    public static NormalisedImage Refuse(string reason) => new(null, 0, 0, reason);
}
