using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;
using SkiaSharp;

namespace AffiVideo.Api.Tests;

public sealed class ProductAssetTests(AffiVideoApp app)
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_uploads_several_photos_and_a_logo_and_sees_them_on_the_Product()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;

        var front = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Jpeg, 120, 80));
        var back = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Jpeg, 80, 120));
        var logo = await UploadedAsync(member, product, ProductAssetKind.Logo, Image(SKEncodedImageFormat.Png, 64, 64));

        var assets = await member.GetAsync<ProductAssetResponse[]>(Assets(product));
        Assert.Equal([front.Id, back.Id, logo.Id], assets.Select(a => a.Id));
        Assert.Equal([ProductAssetKind.Photo, ProductAssetKind.Photo, ProductAssetKind.Logo], assets.Select(a => a.Kind));
        Assert.Equal([(120, 80), (80, 120), (64, 64)], assets.Select(a => (a.Width, a.Height)));
        Assert.All(assets, asset => Assert.Equal(product, asset.ProductId));
    }

    [Fact]
    public async Task An_upload_answers_with_the_asset_and_where_it_is()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;

        var response = await UploadAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Jpeg, 120, 80));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = await ReadAsync<ProductAssetResponse>(response);
        Assert.Equal($"{Assets(product)}/{asset.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(ProductAssetKind.Photo, asset.Kind);
        Assert.Equal((120, 80), (asset.Width, asset.Height));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task Whatever_kind_of_image_is_uploaded_what_is_served_is_a_PNG_of_the_same_size(SKEncodedImageFormat format)
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(format, 90, 60));

        var served = await member.GetAsync(Content(product, asset.Id));

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        var bytes = await served.Content.ReadAsByteArrayAsync(Cancellation);
        Assert.Equal(PngSignature, bytes[..PngSignature.Length]);
        Assert.Equal(asset.SizeInBytes, bytes.Length);
        using var image = SKBitmap.Decode(bytes);
        Assert.Equal((90, 60), (image.Width, image.Height));
    }

    [Fact]
    public async Task A_PNG_is_stored_pixel_for_pixel_including_what_is_see_through()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        using var logo = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        logo.Erase(SKColors.Transparent);
        logo.SetPixel(1, 2, new SKColor(200, 30, 90, 255));
        logo.SetPixel(3, 0, new SKColor(10, 220, 40, 128));
        using var encoded = logo.Encode(SKEncodedImageFormat.Png, 100);

        var asset = await UploadedAsync(member, product, ProductAssetKind.Logo, encoded.ToArray());

        using var stored = SKBitmap.Decode(
            await member.Http.GetByteArrayAsync(Content(product, asset.Id), Cancellation),
            new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(logo.Pixels, stored.Pixels);
    }

    [Fact]
    public async Task What_is_stored_is_the_picture_and_nothing_else_that_was_in_the_file()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var hidden = "hidden-after-the-picture"u8.ToArray();
        byte[] png = [.. Image(SKEncodedImageFormat.Png, 8, 8), .. hidden];

        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, png);

        var stored = await member.Http.GetByteArrayAsync(Content(product, asset.Id), Cancellation);
        Assert.Equal(-1, stored.AsSpan().IndexOf(hidden));
        using var image = SKBitmap.Decode(stored);
        Assert.Equal((8, 8), (image.Width, image.Height));
    }

    [Fact]
    public async Task A_photo_in_a_wider_colour_space_than_usual_keeps_its_colours()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        // Display P3, which is what a recent phone camera writes.
        using var displayP3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        using var photo = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Opaque, displayP3));
        photo.Erase(new SKColor(255, 0, 0));
        using var encoded = photo.Encode(SKEncodedImageFormat.Png, 100);

        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, encoded.ToArray());

        using var data = SKData.CreateCopy(await member.Http.GetByteArrayAsync(Content(product, asset.Id), Cancellation));
        using var stored = SKCodec.Create(data);
        Assert.False(stored.Info.ColorSpace.IsSrgb);
        // The same primaries, to the precision a colour profile is written with.
        Assert.Equal(displayP3.ToColorSpaceXyz().Values, stored.Info.ColorSpace.ToColorSpaceXyz().Values, (a, b) => Math.Abs(a - b) < 0.001f);
        using var pixels = SKBitmap.Decode(stored, stored.Info);
        Assert.Equal(photo.Pixels, pixels.Pixels);
    }

    [Fact]
    public async Task A_photo_taken_with_the_camera_on_its_side_is_stored_upright()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        // 40 by 20, red on the left and blue on the right, marked as needing a quarter turn clockwise.
        using var sideways = new SKBitmap(40, 20);
        using (var canvas = new SKCanvas(sideways))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, 20, 20, new SKPaint { Color = SKColors.Red });
        }
        using var jpeg = sideways.Encode(SKEncodedImageFormat.Jpeg, 95);

        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, WithOrientation(jpeg.ToArray(), 6));

        Assert.Equal((20, 40), (asset.Width, asset.Height));
        using var stored = SKBitmap.Decode(await member.Http.GetByteArrayAsync(Content(product, asset.Id), Cancellation));
        var top = stored.GetPixel(10, 5);
        var bottom = stored.GetPixel(10, 35);
        Assert.True(top.Red > 200 && top.Blue < 60, $"The top should be red, and is {top}.");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"The bottom should be blue, and is {bottom}.");
    }

    [Fact]
    public async Task An_upload_is_judged_by_what_is_in_it_not_by_its_name_or_the_type_it_claims()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;

        var imageCalledText = await UploadAsync(
            member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Jpeg, 30, 30), "notes.txt", "text/plain");
        var textCalledImage = await UploadAsync(
            member, product, ProductAssetKind.Photo, "This is not a picture."u8.ToArray(), "photo.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, imageCalledText.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, textCalledImage.StatusCode);
        Assert.Contains("not a JPEG, PNG or WebP image", await ReasonAsync(textCalledImage));
        Assert.Single(await member.GetAsync<ProductAssetResponse[]>(Assets(product)));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "empty", "empty" },
        { "too large", "larger than 20 MB" },
        { "too wide", "6001 by 10 pixels" },
        { "too tall", "10 by 6001 pixels" },
        { "cut short", "damaged" },
        { "a GIF", "not a JPEG, PNG or WebP image" },
        { "an SVG", "not a JPEG, PNG or WebP image" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task An_upload_that_cannot_be_used_is_refused_with_the_reason_and_nothing_is_kept(string what, string reason)
    {
        var (member, organization, product) = await MemberWithProductAsync();
        using var _ = member;
        var file = what switch
        {
            "empty" => [],
            "too large" => new byte[ProductAsset.MaxUploadBytes + 1],
            "too wide" => Image(SKEncodedImageFormat.Png, ProductAsset.MaxDimension + 1, 10),
            "too tall" => Image(SKEncodedImageFormat.Png, 10, ProductAsset.MaxDimension + 1),
            "cut short" => Image(SKEncodedImageFormat.Jpeg, 400, 400)[..600],
            "a GIF" => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"),
            "an SVG" => "<svg xmlns='http://www.w3.org/2000/svg' width='10' height='10'><rect width='10' height='10'/></svg>"u8.ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        var response = await UploadAsync(member, product, ProductAssetKind.Photo, file);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await ReasonAsync(response));
        Assert.Empty(await member.GetAsync<ProductAssetResponse[]>(Assets(product)));
        Assert.Empty(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
    }

    [Fact]
    public async Task An_image_exactly_at_the_dimension_limit_is_accepted()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;

        var asset = await UploadedAsync(
            member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, ProductAsset.MaxDimension, 8));

        Assert.Equal((ProductAsset.MaxDimension, 8), (asset.Width, asset.Height));
    }

    [Fact]
    public async Task An_upload_with_no_file_or_no_such_kind_is_refused()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        using var noFile = new MultipartFormDataContent { { new StringContent("Photo"), "kind" } };
        using var noSuchKind = Form("Video", Image(SKEncodedImageFormat.Png, 8, 8));
        using var kindByNumber = Form("7", Image(SKEncodedImageFormat.Png, 8, 8));

        var withoutFile = await member.PostFormAsync(Assets(product), noFile);
        var withoutKind = await member.PostFormAsync(Assets(product), noSuchKind);
        var withNumber = await member.PostFormAsync(Assets(product), kindByNumber);

        Assert.Equal(HttpStatusCode.BadRequest, withoutFile.StatusCode);
        Assert.Contains("Choose a file", await ReasonAsync(withoutFile));
        Assert.Equal(HttpStatusCode.BadRequest, withoutKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withNumber.StatusCode);
        Assert.Empty(await member.GetAsync<ProductAssetResponse[]>(Assets(product)));
    }

    [Fact]
    public async Task A_new_logo_takes_the_place_of_the_old_one()
    {
        var (member, organization, product) = await MemberWithProductAsync();
        using var _ = member;
        var photo = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));
        var first = await UploadedAsync(member, product, ProductAssetKind.Logo, Image(SKEncodedImageFormat.Png, 16, 16));

        var second = await UploadedAsync(member, product, ProductAssetKind.Logo, Image(SKEncodedImageFormat.Png, 32, 32));

        var assets = await member.GetAsync<ProductAssetResponse[]>(Assets(product));
        Assert.Equal([photo.Id, second.Id], assets.Select(a => a.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Content(product, first.Id))).StatusCode);
        Assert.Equal(2, (await app.StoredKeysAsync($"organizations/{organization.Id}/")).Length);
    }

    [Fact]
    public async Task A_Product_takes_only_so_many_photos()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var photo = Image(SKEncodedImageFormat.Png, 4, 4);
        for (var i = 0; i < ProductAsset.MaxPhotos; i++) await UploadedAsync(member, product, ProductAssetKind.Photo, photo);

        var oneMore = await UploadAsync(member, product, ProductAssetKind.Photo, photo);
        var logo = await UploadAsync(member, product, ProductAssetKind.Logo, photo);

        Assert.Equal(HttpStatusCode.BadRequest, oneMore.StatusCode);
        Assert.Contains("at most 30 photos", await ReasonAsync(oneMore));
        Assert.Equal(HttpStatusCode.Created, logo.StatusCode);
    }

    [Fact]
    public async Task A_member_removes_an_asset_and_its_file_goes_with_it()
    {
        var (member, organization, product) = await MemberWithProductAsync();
        using var _ = member;
        var kept = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));
        var removed = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));

        var response = await member.DeleteAsync($"{Assets(product)}/{removed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([kept.Id], (await member.GetAsync<ProductAssetResponse[]>(Assets(product))).Select(a => a.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Content(product, removed.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.DeleteAsync($"{Assets(product)}/{removed.Id}")).StatusCode);
        var stored = Assert.Single(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
        Assert.Contains(kept.Id.ToString(), stored);
    }

    [Fact]
    public async Task A_file_is_stored_under_its_Organization_and_cannot_be_fetched_from_the_storage_directly()
    {
        var (member, organization, product) = await MemberWithProductAsync();
        using var _ = member;
        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));

        var key = Assert.Single(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
        using var anyone = new HttpClient();
        var aroundTheApi = await anyone.GetAsync(app.StorageAddress(key), Cancellation);
        var throughTheApi = await member.GetAsync($"/api/v1/{key}");

        Assert.Contains(asset.Id.ToString(), key);
        Assert.Equal(HttpStatusCode.Forbidden, aroundTheApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, throughTheApi.StatusCode);
    }

    [Fact]
    public async Task An_asset_is_only_found_under_the_Product_it_belongs_to()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var other = (await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Another"))).Id;
        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));

        var content = await member.GetAsync(Content(other, asset.Id));
        var remove = await member.DeleteAsync($"{Assets(other)}/{asset.Id}");

        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Single(await member.GetAsync<ProductAssetResponse[]>(Assets(product)));
    }

    [Fact]
    public async Task Assets_of_a_Product_that_does_not_exist_are_not_found()
    {
        var (member, _, _) = await MemberWithProductAsync();
        using var _ = member;
        var nowhere = Guid.NewGuid();

        var list = await member.GetAsync(Assets(nowhere));
        var upload = await UploadAsync(member, nowhere, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
    }

    [Fact]
    public async Task Assets_are_refused_to_someone_who_is_not_signed_in()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync(Assets(product));
        var content = await stranger.GetAsync(Content(product, asset.Id));
        var upload = await UploadAsync(stranger, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));
        var remove = await stranger.DeleteAsync($"{Assets(product)}/{asset.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, content.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, remove.StatusCode);
    }

    [Fact]
    public async Task An_upload_without_an_anti_forgery_token_is_refused()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        using var form = Form("Photo", Image(SKEncodedImageFormat.Png, 8, 8));

        var response = await member.Http.PostAsync(Assets(product), form, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await member.GetAsync<ProductAssetResponse[]>(Assets(product)));
    }

    [Fact]
    public async Task A_browser_that_already_holds_the_image_is_told_so_only_after_asking_again()
    {
        var (member, _, product) = await MemberWithProductAsync();
        using var _ = member;
        var asset = await UploadedAsync(member, product, ProductAssetKind.Photo, Image(SKEncodedImageFormat.Png, 8, 8));
        var first = await member.GetAsync(Content(product, asset.Id));
        using var again = new HttpRequestMessage(HttpMethod.Get, Content(product, asset.Id));
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var stranger = app.NewBrowser();
        using var strangerAgain = new HttpRequestMessage(HttpMethod.Get, Content(product, asset.Id));
        strangerAgain.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        var unchanged = await member.Http.SendAsync(again, Cancellation);
        var refused = await stranger.Http.SendAsync(strangerAgain, Cancellation);

        Assert.True(first.Headers.CacheControl is { Private: true, NoCache: true });
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    internal static string Assets(Guid productId) => $"/api/v1/products/{productId}/assets";

    internal static string Content(Guid productId, Guid assetId) => $"{Assets(productId)}/{assetId}/content";

    /// <summary>A small real image of this format and size.</summary>
    internal static byte[] Image(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Teal);
        using var encoded = bitmap.Encode(format, 90);
        return encoded.ToArray();
    }

    internal static async Task<HttpResponseMessage> UploadAsync(
        Browser member, Guid productId, ProductAssetKind kind, byte[] file,
        string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        using var form = Form(kind.ToString(), file, fileName, contentType);
        return await member.PostFormAsync(Assets(productId), form);
    }

    internal static async Task<ProductAssetResponse> UploadedAsync(Browser member, Guid productId, ProductAssetKind kind, byte[] file)
    {
        var response = await UploadAsync(member, productId, kind, file);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ProductAssetResponse>(response);
    }

    private static MultipartFormDataContent Form(
        string kind, byte[] file, string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { new StringContent(kind), "kind" }, { content, "file", fileName } };
    }

    // A JPEG with a note for the viewer about which way is up, as a camera writes it:
    // an Exif block holding only the orientation, straight after the start of the file.
    private static byte[] WithOrientation(byte[] jpeg, byte orientation)
    {
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
            0x00, 0x01,
            0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, orientation, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    // What the member is told is wrong with the file.
    private static async Task<string> ReasonAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        return problem.Errors.TryGetValue("file", out var reasons) ? string.Join(" ", reasons) : "";
    }

    private async Task<(Browser Member, TestOrganization Organization, Guid ProductId)> MemberWithProductAsync()
    {
        var organization = await app.CreateOrganizationAsync();
        var member = await app.SignedInAsync(organization.Owner);
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        return (member, organization, product.Id);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
