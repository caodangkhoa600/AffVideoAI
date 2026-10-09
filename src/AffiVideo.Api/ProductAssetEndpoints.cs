using AffiVideo.Application.Products;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AffiVideo.Api;

internal static class ProductAssetEndpoints
{
    // Room for the form around the file, so that a file a little over the limit
    // is refused with the reason and not cut off by the server.
    private const long LargestRequest = ProductAsset.MaxUploadBytes + 1024 * 1024;

    // The photos and logo of one of the caller's Organization's Products. As with
    // the Product itself, another Organization's is answered like one that does not exist: 404.
    public static void MapProductAssets(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/products/{productId:guid}/assets").WithTags("Product assets");

        group.MapGet("", async Task<Results<Ok<ProductAssetResponse[]>, NotFound>> (
                Guid productId, IProductAssets assets, CancellationToken cancellationToken) =>
                await assets.ListAsync(productId, cancellationToken) is { } found
                    ? TypedResults.Ok(found.Select(ToResponse).ToArray())
                    : TypedResults.NotFound())
            .WithName("ListProductAssets")
            .WithSummary("The Product's photos and logo, oldest first.");

        group.MapPost("", async Task<Results<Created<ProductAssetResponse>, ValidationProblem, NotFound>> (
                Guid productId, [FromForm] ProductAssetKind kind, IFormFile? file,
                IProductAssets assets, CancellationToken cancellationToken) =>
            {
                // A number is read as a kind too, and "7" is no kind at all.
                if (!Enum.IsDefined(kind))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Choose Photo or Logo."] });
                }
                if (file is null) return Refused("Choose a file to upload.");

                // Only the bytes are passed on. The file's name and the type the browser declared are not.
                await using var upload = file.OpenReadStream();
                var added = await assets.AddAsync(productId, kind, upload, cancellationToken);
                return added switch
                {
                    null => TypedResults.NotFound(),
                    { Asset: { } asset } => TypedResults.Created($"/api/v1/products/{productId}/assets/{asset.Id}", ToResponse(asset)),
                    _ => Refused(added.Refused!),
                };
            })
            // Reading a form asks for the framework's anti-forgery middleware. Every
            // state-changing request has already been through RequireAntiforgeryToken.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(LargestRequest))
            .WithName("UploadProductAsset")
            .WithSummary(
                "Adds a photo, or sets the logo, from a JPEG, PNG or WebP file. The file is decoded and kept as a PNG; " +
                "a new logo takes the place of the old one.");

        group.MapGet("/{assetId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid productId, Guid assetId, IProductAssets assets, HttpContext context, CancellationToken cancellationToken) =>
            {
                var found = await assets.OpenAsync(productId, assetId, cancellationToken);
                if (found is null) return TypedResults.NotFound();

                // A browser may keep the image, but asks before showing it again, so
                // every showing is authorised. An asset's content never changes.
                context.Response.Headers.CacheControl = "private, no-cache";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.Stream(
                    found.Content, ProductAsset.ContentType, entityTag: new EntityTagHeaderValue($"\"{found.Asset.Id:N}\""));
            })
            .Produces(StatusCodes.Status200OK, contentType: ProductAsset.ContentType)
            .WithName("GetProductAssetContent")
            .WithSummary("The image, as a PNG.");

        group.MapDelete("/{assetId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid productId, Guid assetId, IProductAssets assets, CancellationToken cancellationToken) =>
                await assets.RemoveAsync(productId, assetId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("RemoveProductAsset")
            .WithSummary("Removes a photo or the logo, and its file.");
    }

    private static ValidationProblem Refused(string reason) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [reason] });

    private static ProductAssetResponse ToResponse(ProductAsset asset) => new(
        asset.Id,
        asset.ProductId,
        asset.Kind,
        asset.Width,
        asset.Height,
        asset.SizeInBytes,
        asset.CreatedAt);
}
