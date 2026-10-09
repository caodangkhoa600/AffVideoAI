using SkiaSharp;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// An image as plain bytes: red, green, blue and alpha for each pixel, row by
/// row, with alpha not multiplied in. Everything done to a Product's photo is
/// done on these, where it can be seen that a pixel was copied and not redrawn.
/// </summary>
/// <param name="ColorSpace">The photo's own, carried along so no colour is converted.</param>
public sealed record Pixels(int Width, int Height, byte[] Rgba, SKColorSpace? ColorSpace = null)
{
    public static Pixels Blank(int width, int height, SKColorSpace? colorSpace = null) =>
        new(width, height, new byte[width * height * 4], colorSpace);

    public byte Alpha(int x, int y) => Rgba[(y * Width + x) * 4 + 3];

    public static Pixels Decode(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("The file is not an image.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, codec.Info.ColorSpace);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            throw new InvalidDataException("The image is damaged and cannot be read.");
        }
        return new Pixels(info.Width, info.Height, bitmap.GetPixelSpan().ToArray(), info.ColorSpace);
    }

    public byte[] EncodePng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, ColorSpace));
        Rgba.CopyTo(bitmap.GetPixelSpan());
        using var pixmap = bitmap.PeekPixels();
        using var png = pixmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The image could not be written as a PNG.");
        return png.ToArray();
    }

    /// <summary>The part of the image inside the rectangle.</summary>
    public Pixels Crop(int left, int top, int width, int height)
    {
        var cropped = Blank(width, height, ColorSpace);
        for (var y = 0; y < height; y++)
        {
            Rgba.AsSpan(((top + y) * Width + left) * 4, width * 4).CopyTo(cropped.Rgba.AsSpan(y * width * 4));
        }
        return cropped;
    }

    /// <summary>
    /// The image at another size. Colour is averaged with alpha multiplied in, so
    /// what shows through an edge does not take on the colour of what is hidden.
    /// </summary>
    public Pixels Resize(int width, int height)
    {
        var premultiplied = new float[Width * Height * 4];
        for (var i = 0; i < Width * Height; i++)
        {
            var alpha = Rgba[i * 4 + 3] / 255f;
            premultiplied[i * 4] = Rgba[i * 4] * alpha;
            premultiplied[i * 4 + 1] = Rgba[i * 4 + 1] * alpha;
            premultiplied[i * 4 + 2] = Rgba[i * 4 + 2] * alpha;
            premultiplied[i * 4 + 3] = Rgba[i * 4 + 3];
        }
        var scaled = Resampling.Resize(premultiplied, Width, Height, 4, width, height);
        var resized = Blank(width, height, ColorSpace);
        for (var i = 0; i < width * height; i++)
        {
            var alpha = scaled[i * 4 + 3] / 255f;
            for (var c = 0; c < 3; c++)
            {
                resized.Rgba[i * 4 + c] = alpha <= 0 ? (byte)0 : ToByte(scaled[i * 4 + c] / alpha);
            }
            resized.Rgba[i * 4 + 3] = ToByte(scaled[i * 4 + 3]);
        }
        return resized;
    }

    public static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);
}

/// <summary>Resizing and blurring of plain arrays of numbers, one or more to a pixel.</summary>
public static class Resampling
{
    /// <summary>Smaller by averaging the pixels each new one covers; larger by blending between neighbours.</summary>
    public static float[] Resize(float[] source, int width, int height, int channels, int newWidth, int newHeight)
    {
        var across = new float[newWidth * height * channels];
        var columns = Weights(width, newWidth);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < newWidth; x++)
            {
                foreach (var (from, weight) in columns[x])
                {
                    for (var c = 0; c < channels; c++)
                    {
                        across[(y * newWidth + x) * channels + c] += source[(y * width + from) * channels + c] * weight;
                    }
                }
            }
        }

        var result = new float[newWidth * newHeight * channels];
        var rows = Weights(height, newHeight);
        for (var y = 0; y < newHeight; y++)
        {
            foreach (var (from, weight) in rows[y])
            {
                var target = result.AsSpan(y * newWidth * channels, newWidth * channels);
                var line = across.AsSpan(from * newWidth * channels, newWidth * channels);
                for (var i = 0; i < target.Length; i++) target[i] += line[i] * weight;
            }
        }
        return result;
    }

    // For each new position along one side, the old positions it is made from and how much of each.
    private static (int From, float Weight)[][] Weights(int size, int newSize)
    {
        var weights = new (int, float)[newSize][];
        var scale = (double)size / newSize;
        for (var i = 0; i < newSize; i++)
        {
            if (scale <= 1)
            {
                var at = Math.Clamp((i + 0.5) * scale - 0.5, 0, size - 1);
                var first = (int)Math.Floor(at);
                var second = Math.Min(first + 1, size - 1);
                var blend = (float)(at - first);
                weights[i] = [(first, 1 - blend), (second, blend)];
                continue;
            }

            double start = i * scale, end = Math.Min((i + 1) * scale, size);
            var covered = new List<(int, float)>();
            for (var from = (int)Math.Floor(start); from < end; from++)
            {
                var share = Math.Min(from + 1, end) - Math.Max(from, start);
                if (share > 0) covered.Add((from, (float)(share / (end - start))));
            }
            weights[i] = [.. covered];
        }
        return weights;
    }

    /// <summary>A soft blur of about this radius (the standard deviation of a Gaussian), in place.</summary>
    public static void Blur(float[] values, int width, int height, double sigma)
    {
        // Three passes of a plain average come close to a Gaussian.
        var radius = Math.Max(1, (int)Math.Round(Math.Sqrt(sigma * sigma + 1) - 0.5));
        var line = new float[Math.Max(width, height)];
        for (var pass = 0; pass < 3; pass++)
        {
            for (var y = 0; y < height; y++) Average(values, y * width, 1, width, radius, line);
            for (var x = 0; x < width; x++) Average(values, x, width, height, radius, line);
        }
    }

    // What lies beyond either end counts as nothing.
    private static void Average(float[] values, int start, int step, int count, int radius, float[] line)
    {
        float sum = 0;
        for (var i = 0; i < Math.Min(radius, count); i++) sum += values[start + i * step];
        for (var i = 0; i < count; i++)
        {
            if (i + radius < count) sum += values[start + (i + radius) * step];
            if (i - radius - 1 >= 0) sum -= values[start + (i - radius - 1) * step];
            line[i] = sum / (2 * radius + 1);
        }
        for (var i = 0; i < count; i++) values[start + i * step] = line[i];
    }
}
