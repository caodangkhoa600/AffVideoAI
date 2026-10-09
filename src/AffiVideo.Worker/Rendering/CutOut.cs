namespace AffiVideo.Worker.Rendering;

/// <summary>
/// The Product cut out of its photo. Only the photo's transparency is decided
/// here: a cut-out is the photo's own pixels with a new alpha, and one that
/// fails its checks is not used.
/// </summary>
public static class CutOut
{
    // A cut-out outside these limits is rejected. Measured on the founder's photos in ticket 25.
    private const double MinCover = 0.02; // share of the photo kept: below this, nothing was found
    private const double MaxCover = 0.92; // above this, the background was kept too
    private const double MaxSoft = 0.08; // share of what shows that is half-transparent: parts of the Product are missing
    private const double MaxHalo = 0.12; // share of the edge that is still the colour of the background

    // The checks are shares of the image, so a large photo is measured on every nth pixel.
    private const int LargestMeasuredSide = 2048;

    /// <summary>Whether the photo arrived with a background already removed.</summary>
    public static bool HasOwnTransparency(Pixels photo)
    {
        var clear = 0;
        for (var i = 3; i < photo.Rgba.Length; i += 4)
        {
            if (photo.Rgba[i] < 8) clear++;
        }
        return clear > 0.02 * photo.Width * photo.Height;
    }

    /// <summary>
    /// The photo as taken, with nothing see-through: its own colours wherever it
    /// shows at all, and white where it does not. Half-transparent pixels are not
    /// blended with white, or the Product's edge would be repainted.
    /// </summary>
    public static Pixels Uncut(Pixels photo)
    {
        var whole = photo with { Rgba = (byte[])photo.Rgba.Clone() };
        for (var i = 0; i < whole.Rgba.Length; i += 4)
        {
            if (whole.Rgba[i + 3] == 0) whole.Rgba[i] = whole.Rgba[i + 1] = whole.Rgba[i + 2] = 255;
            whole.Rgba[i + 3] = 255;
        }
        return whole;
    }

    /// <summary>
    /// The cut-out with nothing showing where the photo itself showed nothing. A
    /// photo that came with see-through parts was made whole with white before it
    /// was cut, and that white is not the Product's: it is never shown.
    /// </summary>
    public static Pixels HidingWhatThePhotoHid(Pixels cutOut, Pixels photo)
    {
        var hidden = cutOut with { Rgba = (byte[])cutOut.Rgba.Clone() };
        for (var i = 3; i < hidden.Rgba.Length; i += 4)
        {
            if (photo.Rgba[i] == 0) hidden.Rgba[i] = 0;
        }
        return hidden;
    }

    /// <summary>
    /// Whether everything the cut-out shows is the photo's own pixel. A cut-out
    /// that changed a colour has repainted the Product, and is not used.
    /// </summary>
    public static bool ShowsOnlyThePhoto(Pixels cutOut, Pixels photo)
    {
        if (cutOut.Width != photo.Width || cutOut.Height != photo.Height) return false;
        for (var i = 0; i < cutOut.Rgba.Length; i += 4)
        {
            if (cutOut.Rgba[i + 3] == 0) continue;
            if (cutOut.Rgba[i] != photo.Rgba[i] || cutOut.Rgba[i + 1] != photo.Rgba[i + 1] || cutOut.Rgba[i + 2] != photo.Rgba[i + 2])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The reasons not to use a cut-out. Empty when it passes its checks.</summary>
    /// <param name="byModel">
    /// Whether a model removed the background. Only then is there a background left
    /// in the hidden pixels to compare the edge with.
    /// </param>
    public static IReadOnlyList<string> Problems(Pixels cutOut, bool byModel)
    {
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(cutOut.Width, cutOut.Height) / (double)LargestMeasuredSide));
        int width = (cutOut.Width + step - 1) / step, height = (cutOut.Height + step - 1) / step;
        var alpha = new byte[width * height];
        var colour = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var from = (y * step * cutOut.Width + x * step) * 4;
                alpha[y * width + x] = cutOut.Rgba[from + 3];
                cutOut.Rgba.AsSpan(from, 3).CopyTo(colour.AsSpan((y * width + x) * 3));
            }
        }

        int kept = 0, visible = 0, soft = 0, removed = 0;
        foreach (var a in alpha)
        {
            if (a > 127) kept++;
            if (a > 12) visible++;
            if (a is > 12 and < 243) soft++;
            if (a < 12) removed++;
        }

        var problems = new List<string>();
        var cover = (double)kept / alpha.Length;
        if (cover < MinCover) problems.Add("no Product was found in the photo");
        else if (cover > MaxCover) problems.Add("the background was kept");
        if ((double)soft / Math.Max(1, visible) > MaxSoft) problems.Add("parts of the Product are missing");

        // A halo can only be measured against a plain background.
        if (byModel && removed > 1000 && kept > 0 && PlainBackground(alpha, colour) is { } background)
        {
            var reach = Math.Max(2, (int)Math.Round(0.004 * Math.Min(width, height)));
            var depth = DepthInsideKept(alpha, width, height, reach);
            int edge = 0, likeBackground = 0;
            for (var i = 0; i < alpha.Length; i++)
            {
                if (alpha[i] <= 127 || depth[i] > reach) continue;
                edge++;
                if (Difference(colour, i, background) < 14) likeBackground++;
            }
            if (edge > 0 && (double)likeBackground / edge > MaxHalo) problems.Add("background was left around the Product's edge");
        }
        return problems;
    }

    // The colour of what was removed, when it is near enough one colour. Null when it is not.
    private static (int R, int G, int B)? PlainBackground(byte[] alpha, byte[] colour)
    {
        var counts = new int[3][] { new int[256], new int[256], new int[256] };
        var removed = 0;
        for (var i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] >= 12) continue;
            removed++;
            for (var c = 0; c < 3; c++) counts[c][colour[i * 3 + c]]++;
        }
        var background = (Median(counts[0], removed), Median(counts[1], removed), Median(counts[2], removed));

        long spread = 0;
        for (var i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] < 12) spread += Difference(colour, i, background);
        }
        return (double)spread / removed < 10 ? background : null;
    }

    private static int Median(int[] counts, int total)
    {
        var seen = 0;
        for (var value = 0; value < counts.Length; value++)
        {
            seen += counts[value];
            if (seen * 2 >= total) return value;
        }
        return 255;
    }

    private static int Difference(byte[] colour, int pixel, (int R, int G, int B) other) => Math.Max(
        Math.Abs(colour[pixel * 3] - other.R),
        Math.Max(Math.Abs(colour[pixel * 3 + 1] - other.G), Math.Abs(colour[pixel * 3 + 2] - other.B)));

    // For each kept pixel, how many steps up, down, left or right reach a pixel that
    // is not kept, counted no further than the limit. The image's border is not kept.
    private static int[] DepthInsideKept(byte[] alpha, int width, int height, int limit)
    {
        var depth = new int[alpha.Length];
        var far = limit + 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                if (alpha[i] <= 127) continue;
                var above = y == 0 ? 0 : depth[i - width];
                var before = x == 0 ? 0 : depth[i - 1];
                depth[i] = Math.Min(far, Math.Min(above, before) + 1);
            }
        }
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = width - 1; x >= 0; x--)
            {
                var i = y * width + x;
                if (alpha[i] <= 127) continue;
                var below = y == height - 1 ? 0 : depth[i + width];
                var after = x == width - 1 ? 0 : depth[i + 1];
                depth[i] = Math.Min(depth[i], Math.Min(below, after) + 1);
            }
        }
        return depth;
    }

    // Where the Product sits when a photo is shown whole, at its largest: the corner radius is 36 pixels there.
    private const double CardMaxWidth = 860, CardMaxHeight = 1060, CardMaxUpscale = 1.5, CardRadius = 36;

    /// <summary>The whole photo with rounded corners, for when the Product cannot be cut out of it.</summary>
    public static Pixels AsCard(Pixels photo)
    {
        var card = Uncut(photo);
        var scale = Math.Min(Math.Min(CardMaxWidth / card.Width, CardMaxHeight / card.Height), CardMaxUpscale);
        var radius = Math.Min(CardRadius / scale, Math.Min(card.Width, card.Height) / 2.0);
        var reach = (int)Math.Ceiling(radius);
        for (var y = 0; y < card.Height; y++)
        {
            var nearTop = y < reach;
            if (!nearTop && y < card.Height - reach) continue;
            for (var x = 0; x < card.Width; x++)
            {
                var nearLeft = x < reach;
                if (!nearLeft && x < card.Width - reach) continue;
                // How far the pixel's centre is from the centre of its corner's arc.
                var dx = (nearLeft ? radius - x : x - (card.Width - radius)) - 0.5 * (nearLeft ? 1 : -1);
                var dy = (nearTop ? radius - y : y - (card.Height - radius)) - 0.5 * (nearTop ? 1 : -1);
                if (dx <= 0 || dy <= 0) continue;
                var inside = Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + 0.5, 0, 1);
                card.Rgba[(y * card.Width + x) * 4 + 3] = (byte)Math.Round(inside * 255);
            }
        }
        return card;
    }
}
