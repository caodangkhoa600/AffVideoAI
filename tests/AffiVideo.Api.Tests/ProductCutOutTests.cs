using AffiVideo.Worker.Rendering;

namespace AffiVideo.Api.Tests;

/// <summary>
/// What is done to a Product's photo before a video shows it, as pure functions
/// on pixels: the checks a cut-out has to pass, the card a photo falls back to,
/// and the layer with its shadow. Throughout, the Product's own pixels are never repainted.
/// </summary>
public sealed class ProductCutOutTests
{
    private static readonly (byte R, byte G, byte B) Blue = (24, 92, 140);
    private static readonly (byte R, byte G, byte B) Backdrop = (236, 233, 228);

    [Fact]
    public void A_cut_out_with_a_clean_edge_passes_its_checks_whoever_made_it()
    {
        var cutOut = Photo(400, 400, Backdrop, alpha: 0);
        Fill(cutOut, 100, 80, 300, 320, Blue, alpha: 255);

        Assert.Empty(CutOut.Problems(cutOut, byModel: false));
        Assert.Empty(CutOut.Problems(cutOut, byModel: true));
    }

    [Fact]
    public void A_cut_out_that_kept_almost_nothing_or_almost_everything_is_rejected()
    {
        var nothing = Photo(400, 400, Backdrop, alpha: 0);
        Fill(nothing, 0, 0, 40, 40, Blue, alpha: 255);
        var everything = Photo(400, 400, Blue, alpha: 255);

        Assert.Equal(["no Product was found in the photo"], CutOut.Problems(nothing, byModel: true));
        Assert.Equal(["the background was kept"], CutOut.Problems(everything, byModel: true));
    }

    [Fact]
    public void A_cut_out_with_a_large_half_transparent_part_is_rejected_as_missing_parts_of_the_Product()
    {
        var cutOut = Photo(400, 400, Backdrop, alpha: 0);
        Fill(cutOut, 100, 80, 300, 320, Blue, alpha: 255);
        // The model was unsure about the screen in the middle of it.
        Fill(cutOut, 150, 120, 250, 220, Blue, alpha: 120);

        Assert.Equal(["parts of the Product are missing"], CutOut.Problems(cutOut, byModel: true));
    }

    [Fact]
    public void A_cut_out_by_the_model_that_left_a_rim_of_plain_background_is_rejected_and_one_from_the_photo_itself_is_not_measured()
    {
        // The photo: a Product on a plain backdrop. The cut-out keeps eight pixels of backdrop all round it.
        var cutOut = Photo(400, 400, Backdrop, alpha: 0);
        Fill(cutOut, 100, 80, 300, 320, Blue, alpha: 255);
        for (var y = 72; y < 328; y++)
        {
            for (var x = 92; x < 308; x++) cutOut.Rgba[(y * 400 + x) * 4 + 3] = 255;
        }

        Assert.Equal(["background was left around the Product's edge"], CutOut.Problems(cutOut, byModel: true));
        // A photo that came already cut out has no background left to compare its edge with.
        Assert.Empty(CutOut.Problems(cutOut, byModel: false));
    }

    [Fact]
    public void A_halo_is_not_looked_for_on_a_busy_background()
    {
        var cutOut = Photo(400, 400, Backdrop, alpha: 0);
        for (var i = 0; i < 400 * 400; i++)
        {
            cutOut.Rgba[i * 4] = (byte)(i * 37 % 256);
            cutOut.Rgba[i * 4 + 1] = (byte)(i * 91 % 256);
        }
        Fill(cutOut, 100, 80, 300, 320, Blue, alpha: 255);

        Assert.Empty(CutOut.Problems(cutOut, byModel: true));
    }

    [Fact]
    public void A_cut_out_that_changed_a_pixel_it_shows_is_told_apart_from_one_that_only_decided_transparency()
    {
        var photo = Photo(40, 40, Backdrop, alpha: 255);
        Fill(photo, 10, 10, 30, 30, Blue, alpha: 255);
        var honest = Photo(40, 40, Backdrop, alpha: 0);
        Fill(honest, 10, 10, 30, 30, Blue, alpha: 255);
        var repainted = honest with { Rgba = (byte[])honest.Rgba.Clone() };
        repainted.Rgba[(20 * 40 + 20) * 4] ^= 1;
        var hidden = honest with { Rgba = (byte[])honest.Rgba.Clone() };
        hidden.Rgba[0] ^= 1; // Fully transparent there: what it holds is never seen.

        Assert.True(CutOut.ShowsOnlyThePhoto(honest, photo));
        Assert.False(CutOut.ShowsOnlyThePhoto(repainted, photo));
        Assert.True(CutOut.ShowsOnlyThePhoto(hidden, photo));
        Assert.False(CutOut.ShowsOnlyThePhoto(Photo(40, 41, Backdrop, alpha: 0), photo));
    }

    [Fact]
    public void A_cut_out_never_shows_what_the_photo_itself_did_not_show()
    {
        // The photo came with a see-through background. Made whole it is white there, and the model kept some of that white.
        var photo = Photo(4, 1, Backdrop, alpha: 0);
        Fill(photo, 2, 0, 4, 1, Blue, alpha: 255);
        var byModel = CutOut.Uncut(photo);
        byModel.Rgba[3] = 0;

        var cutOut = CutOut.HidingWhatThePhotoHid(byModel, photo);

        Assert.Equal([0, 0, 255, 255], new[] { cutOut.Alpha(0, 0), cutOut.Alpha(1, 0), cutOut.Alpha(2, 0), cutOut.Alpha(3, 0) });
        Assert.Equal(255, byModel.Alpha(1, 0)); // What it was given is left as it was.
    }

    [Fact]
    public void A_photo_taken_whole_keeps_its_own_colours_where_it_shows_at_all_and_is_white_where_it_does_not()
    {
        var photo = Photo(3, 1, Backdrop, alpha: 0);
        Fill(photo, 1, 0, 2, 1, Blue, alpha: 90);
        Fill(photo, 2, 0, 3, 1, Blue, alpha: 255);

        var whole = CutOut.Uncut(photo);

        Assert.Equal([255, 255, 255, 255, Blue.R, Blue.G, Blue.B, 255, Blue.R, Blue.G, Blue.B, 255], whole.Rgba);
        Assert.Equal(0, photo.Rgba[3]); // The photo itself is left as it was.
        Assert.True(CutOut.HasOwnTransparency(photo));
        Assert.False(CutOut.HasOwnTransparency(whole));
    }

    [Fact]
    public void A_card_is_the_whole_photo_with_only_its_corners_rounded_away()
    {
        var photo = Photo(600, 800, Blue, alpha: 255);

        var card = CutOut.AsCard(photo);

        Assert.Equal((600, 800), (card.Width, card.Height));
        Assert.All(new[] { (0, 0), (599, 0), (0, 799), (599, 799) }, corner => Assert.Equal(0, card.Alpha(corner.Item1, corner.Item2)));
        Assert.All(new[] { (300, 0), (0, 400), (599, 400), (300, 799), (300, 400), (40, 40) }, inside => Assert.Equal(255, card.Alpha(inside.Item1, inside.Item2)));
        Assert.True(CutOut.ShowsOnlyThePhoto(card, photo));
    }

    [Fact]
    public void A_layer_holds_the_Product_untouched_in_the_middle_with_a_soft_shadow_below_it()
    {
        var subject = Photo(300, 500, Backdrop, alpha: 0);
        Fill(subject, 50, 100, 250, 400, Blue, alpha: 255);
        subject.Rgba[(250 * 300 + 150) * 4] = 201; // One pixel of its own, to be found again.

        var layer = ProductLayer.Make(subject);

        // Trimmed to the Product, with room for the shadow on every side.
        Assert.Equal((200, 300), (layer.ProductWidth, layer.ProductHeight));
        var pad = (layer.Image.Width - 200) / 2;
        Assert.Equal(42, pad);
        Assert.Equal((200 + 2 * pad, 300 + 2 * pad), (layer.Image.Width, layer.Image.Height));
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 200; x++)
            {
                var from = ((y + 100) * 300 + x + 50) * 4;
                var to = ((y + pad) * layer.Image.Width + x + pad) * 4;
                Assert.True(subject.Rgba.AsSpan(from, 4).SequenceEqual(layer.Image.Rgba.AsSpan(to, 4)), $"The Product's pixel at {x}, {y} was changed.");
            }
        }
        // Black, see-through, and deeper under the Product than above it.
        var below = Pixel(layer.Image, pad + 100, pad + 300 + 4);
        var above = Pixel(layer.Image, pad + 100, pad - 5);
        Assert.Equal((0, 0, 0), (below.R, below.G, below.B));
        Assert.InRange(below.A, 20, 120);
        Assert.True(below.A > above.A);
        Assert.Equal(0, layer.Image.Alpha(0, 0));
    }

    [Fact]
    public void A_Product_larger_than_a_frame_needs_is_scaled_down_and_nothing_else()
    {
        var subject = Photo(1000, 3600, Blue, alpha: 255);

        var layer = ProductLayer.Make(subject);

        Assert.Equal((500, 1800), (layer.ProductWidth, layer.ProductHeight));
        var middle = Pixel(layer.Image, layer.Image.Width / 2, layer.Image.Height / 2);
        Assert.Equal((Blue.R, Blue.G, Blue.B, (byte)255), middle);
    }

    [Fact]
    public void The_palette_is_taken_from_the_Products_most_common_saturated_colour_and_is_grey_for_a_grey_Product()
    {
        var blue = Photo(200, 200, Blue, alpha: 255);
        Fill(blue, 0, 0, 200, 60, (250, 250, 250), alpha: 255); // Glare says little about the Product.
        var grey = Photo(200, 200, (128, 128, 128), alpha: 255);

        var (hue, saturation) = ProductLayer.Make(blue).Colour();
        var (_, none) = ProductLayer.Make(grey).Colour();

        Assert.InRange(hue, 0.55, 0.60); // Blue.
        Assert.True(saturation > 0.5);
        Assert.Equal(0, none);
        var palette = Palette.From(hue, saturation);
        Assert.Equal(new Palette("#184462", "#c2daeb", "#cddbe4", "#141416"), palette);
        Assert.Equal(new Palette("#22242a", "#e2e2e4", "#e8e6e2", "#141416"), Palette.From(0, none));
    }

    private static Pixels Photo(int width, int height, (byte R, byte G, byte B) colour, byte alpha)
    {
        var photo = Pixels.Blank(width, height);
        Fill(photo, 0, 0, width, height, colour, alpha);
        return photo;
    }

    private static void Fill(Pixels image, int left, int top, int right, int bottom, (byte R, byte G, byte B) colour, byte alpha)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var i = (y * image.Width + x) * 4;
                (image.Rgba[i], image.Rgba[i + 1], image.Rgba[i + 2], image.Rgba[i + 3]) = (colour.R, colour.G, colour.B, alpha);
            }
        }
    }

    private static (byte R, byte G, byte B, byte A) Pixel(Pixels image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return (image.Rgba[i], image.Rgba[i + 1], image.Rgba[i + 2], image.Rgba[i + 3]);
    }
}
