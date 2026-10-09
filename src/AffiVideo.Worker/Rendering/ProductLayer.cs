namespace AffiVideo.Worker.Rendering;

/// <summary>
/// A Product's photo as a video shows it: the Product (cut out, or whole on a
/// card) centred in a transparent image with its soft shadow already under it,
/// so a template moves one image. Where the Product is solid, its pixels are
/// the photo's pixels.
/// </summary>
/// <param name="ProductWidth">Of the Product within the image, in pixels.</param>
public sealed record ProductLayer(Pixels Image, int ProductWidth, int ProductHeight)
{
    /// <summary>The longest side a Product is kept at. A frame is 1920 pixels tall.</summary>
    public const int LargestSide = 1800;

    /// <param name="subject">The cut-out, or the card.</param>
    public static ProductLayer Make(Pixels subject)
    {
        subject = Trimmed(subject);
        var longest = Math.Max(subject.Width, subject.Height);
        if (longest > LargestSide)
        {
            subject = subject.Resize(
                Math.Max(1, (int)Math.Round((double)subject.Width * LargestSide / longest)),
                Math.Max(1, (int)Math.Round((double)subject.Height * LargestSide / longest)));
        }

        // Room on every side for the shadow, which reaches further the taller the Product is.
        var reach = Math.Max(subject.Width, subject.Height);
        var pad = (int)Math.Round(0.14 * reach);
        int width = subject.Width + 2 * pad, height = subject.Height + 2 * pad;
        var layer = Pixels.Blank(width, height, subject.ColorSpace);
        var alpha = new float[width * height];
        for (var y = 0; y < subject.Height; y++)
        {
            var row = ((y + pad) * width + pad) * 4;
            subject.Rgba.AsSpan(y * subject.Width * 4, subject.Width * 4).CopyTo(layer.Rgba.AsSpan(row));
            for (var x = 0; x < subject.Width; x++) alpha[(y + pad) * width + pad + x] = subject.Rgba[(y * subject.Width + x) * 4 + 3] / 255f;
        }

        // A wide soft shadow and a tighter one, both a little below the Product.
        var wide = Shadow(alpha, width, height, 0.035 * reach, 0.028 * reach);
        var tight = Shadow(alpha, width, height, 0.012 * reach, 0.010 * reach);
        for (var i = 0; i < alpha.Length; i++)
        {
            var shade = Math.Clamp(wide[i] * 0.24f + tight[i] * 0.16f, 0, 1);
            var own = alpha[i];
            if (own >= 1) continue; // Solid: the photo's pixel, untouched.

            // The Product's pixel over a black shadow, with alpha still not multiplied in.
            var together = own + shade * (1 - own);
            for (var c = 0; c < 3; c++)
            {
                layer.Rgba[i * 4 + c] = together <= 0 ? (byte)0 : Pixels.ToByte(layer.Rgba[i * 4 + c] * own / together);
            }
            layer.Rgba[i * 4 + 3] = Pixels.ToByte(together * 255);
        }
        return new ProductLayer(layer, subject.Width, subject.Height);
    }

    // The image without the empty rows and columns around what it shows.
    private static Pixels Trimmed(Pixels image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Alpha(x, y) == 0) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? image : image.Crop(left, top, right - left + 1, bottom - top + 1);
    }

    private static float[] Shadow(float[] alpha, int width, int height, double blur, double drop)
    {
        var shadow = (float[])alpha.Clone();
        Resampling.Blur(shadow, width, height, blur);
        var rows = (int)Math.Round(drop);
        if (rows <= 0) return shadow;
        Array.Copy(shadow, 0, shadow, rows * width, (height - rows) * width);
        Array.Clear(shadow, 0, rows * width);
        return shadow;
    }

    /// <summary>
    /// The Product's most characteristic colour: the hue most of its saturated
    /// pixels share. Near-black and near-white areas (lenses, glare) count for
    /// little, and a Product with no colour to speak of answers with none.
    /// </summary>
    /// <returns>Hue from 0 to 1 and saturation from 0 to 1; saturation 0 for a grey Product.</returns>
    public (double Hue, double Saturation) Colour()
    {
        const int hues = 24;
        var score = new double[hues + 1]; // The last is for pixels with no hue.
        var sum = new double[hues + 1, 3];
        var pixels = Image.Rgba;
        // About fifty thousand pixels, evenly spread: every nth along each row of every nth row.
        var step = Math.Max(1, (int)Math.Sqrt(Image.Width * (double)Image.Height / 50_000));
        for (var y = 0; y < Image.Height; y += step)
        for (var x = 0; x < Image.Width; x += step)
        {
            var i = y * Image.Width + x;
            if (pixels[i * 4 + 3] <= 200) continue;
            double r = pixels[i * 4] / 255.0, g = pixels[i * 4 + 1] / 255.0, b = pixels[i * 4 + 2] / 255.0;
            var (hue, lightness, saturation) = Hls(r, g, b);
            var weight = (saturation + 0.15) * (lightness is < 0.12 or > 0.92 ? 0.2 : 1.0);
            var bucket = saturation < 0.12 ? hues : Math.Min(hues - 1, (int)(hue * hues));
            score[bucket] += weight;
            sum[bucket, 0] += r * weight;
            sum[bucket, 1] += g * weight;
            sum[bucket, 2] += b * weight;
        }

        var best = Array.IndexOf(score, score.Max());
        if (best == hues || score[best] <= 0) return (0, 0);
        var (h, _, s) = Hls(sum[best, 0] / score[best], sum[best, 1] / score[best], sum[best, 2] / score[best]);
        return (h, s);
    }

    private static (double Hue, double Lightness, double Saturation) Hls(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max - min < 1e-9) return (0, lightness, 0);
        var range = max - min;
        var saturation = lightness <= 0.5 ? range / (max + min) : range / (2 - max - min);
        var hue = max == r ? (g - b) / range : max == g ? 2 + (b - r) / range : 4 + (r - g) / range;
        return (((hue / 6) % 1 + 1) % 1, lightness, saturation);
    }
}

/// <summary>The colours a template sets around a Product, taken from the Product's own colour.</summary>
/// <param name="Accent">A deep one for panels and type.</param>
/// <param name="Tint">A pale one for shapes behind the Product.</param>
/// <param name="Ghost">A paler one for type behind the Product.</param>
/// <param name="Ink">For type on the backdrop.</param>
public sealed record Palette(string Accent, string Tint, string Ghost, string Ink)
{
    private const string NearBlack = "#141416";

    public static Palette From(double hue, double saturation)
    {
        // A grey Product has no hue worth borrowing.
        if (saturation < 0.08) return new Palette("#22242a", "#e2e2e4", "#e8e6e2", NearBlack);
        return new Palette(
            Hex(hue, 0.24, Math.Clamp(saturation * 1.5, 0.30, 0.60)),
            Hex(hue, 0.84, Math.Clamp(saturation, 0.25, 0.50)),
            Hex(hue, 0.85, Math.Min(0.30, saturation * 0.7)),
            NearBlack);
    }

    private static string Hex(double hue, double lightness, double saturation)
    {
        var upper = lightness <= 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var lower = 2 * lightness - upper;
        return $"#{Channel(hue + 1.0 / 3):x2}{Channel(hue):x2}{Channel(hue - 1.0 / 3):x2}";

        int Channel(double h)
        {
            h = (h % 1 + 1) % 1;
            var value = h < 1.0 / 6 ? lower + (upper - lower) * 6 * h
                : h < 0.5 ? upper
                : h < 2.0 / 3 ? lower + (upper - lower) * (2.0 / 3 - h) * 6
                : lower;
            return (int)Math.Round(value * 255);
        }
    }
}

/// <summary>The light seamless studio backdrop the founder chose in ticket 25. Drawn, not generated.</summary>
public static class Backdrop
{
    public static Pixels Studio(int width, int height)
    {
        var backdrop = Pixels.Blank(width, height);
        (double R, double G, double B) top = (247, 245, 241), bottom = (231, 228, 222), lit = (255, 255, 254);
        uint random = 2463534242;
        for (var y = 0; y < height; y++)
        {
            var v = (double)y / height;
            for (var x = 0; x < width; x++)
            {
                var u = (double)x / width;
                // A soft light a little above the middle, and edges a touch darker.
                var light = 0.7 * Math.Exp(-(Math.Pow((u - 0.5) / 0.55, 2) + Math.Pow((v - 0.45) / 0.33, 2)));
                var fromCentre = Math.Min(1, Math.Pow((u - 0.5) / 0.75, 2) + Math.Pow((v - 0.5) / 0.75, 2));
                var dim = 1 - 0.10 * Math.Pow(fromCentre, 1.5);
                // A little noise before rounding, the same every time, keeps the gradient from banding.
                random ^= random << 13;
                random ^= random >> 17;
                random ^= random << 5;
                var noise = random / (double)uint.MaxValue * 1.2 - 0.6;

                var i = (y * width + x) * 4;
                backdrop.Rgba[i] = Shade(top.R, bottom.R, lit.R);
                backdrop.Rgba[i + 1] = Shade(top.G, bottom.G, lit.G);
                backdrop.Rgba[i + 2] = Shade(top.B, bottom.B, lit.B);
                backdrop.Rgba[i + 3] = 255;

                byte Shade(double above, double below, double bright)
                {
                    var wall = above + (below - above) * v;
                    return Pixels.ToByte((float)((wall + (bright - wall) * light) * dim + noise));
                }
            }
        }
        return backdrop;
    }
}
