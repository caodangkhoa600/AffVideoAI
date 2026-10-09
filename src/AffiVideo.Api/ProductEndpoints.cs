using AffiVideo.Application;
using AffiVideo.Application.Products;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class ProductEndpoints
{
    // These are the Products of the caller's Organization. A Product of another
    // Organization is answered exactly like one that does not exist: 404.
    public static void MapProducts(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/products").WithTags("Products");

        group.MapGet("", async (
                string? search, string? category, ProductStatus? status, int? page, int? pageSize,
                IProducts products, CancellationToken cancellationToken) =>
            {
                var found = await products.ListAsync(
                    new ProductFilter(search, category, status), new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListProducts")
            .WithSummary("The Organization's Products, by name. Search looks in the name; category and status narrow the list.");

        group.MapGet("/categories", async (IProducts products, CancellationToken cancellationToken) =>
                TypedResults.Ok(await products.ListCategoriesAsync(cancellationToken)))
            .WithName("ListProductCategories")
            .WithSummary("Every category the Organization's Products use, in alphabetical order.");

        group.MapPost("", async Task<Results<Created<ProductResponse>, ValidationProblem>> (
                ProductRequest request, IProducts products, CancellationToken cancellationToken) =>
            {
                var created = await products.CreateAsync(request.ToDetails(), cancellationToken);
                return TypedResults.Created($"/api/v1/products/{created.Id}", ToResponse(created));
            })
            .WithName("CreateProduct")
            .WithSummary("Creates a Product. Its URLs are stored as text and never fetched.");

        group.MapGet("/{productId:guid}", async Task<Results<Ok<ProductResponse>, NotFound>> (
                Guid productId, IProducts products, CancellationToken cancellationToken) =>
                await products.FindAsync(productId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetProduct")
            .WithSummary("One Product.");

        group.MapPut("/{productId:guid}", async Task<Results<Ok<ProductResponse>, ValidationProblem, NotFound>> (
                Guid productId, ProductRequest request, IProducts products, CancellationToken cancellationToken) =>
                await products.ChangeAsync(productId, request.ToDetails(), cancellationToken) is { } changed
                    ? TypedResults.Ok(ToResponse(changed))
                    : TypedResults.NotFound())
            .WithName("UpdateProduct")
            .WithSummary("Replaces a Product's details. An archived Product can still be edited.");

        group.MapPost("/{productId:guid}/archive", async Task<Results<Ok<ProductResponse>, NotFound>> (
                Guid productId, IProducts products, CancellationToken cancellationToken) =>
                await products.ArchiveAsync(productId, cancellationToken) is { } archived
                    ? TypedResults.Ok(ToResponse(archived))
                    : TypedResults.NotFound())
            .WithName("ArchiveProduct")
            .WithSummary("Archives a Product. Archiving one that is already archived changes nothing.");

        group.MapGet("/{productId:guid}/production-cost", async Task<Results<Ok<ProductProductionCostResponse>, NotFound>> (
                Guid productId, IProducts products, CancellationToken cancellationToken) =>
                await products.ProductionCostAsync(productId, cancellationToken) is { } cost
                    ? TypedResults.Ok(new ProductProductionCostResponse(
                        cost.EstimatedTotals.Select(RenderedVideoEndpoints.ToResponse).ToList(), cost.Attempts, cost.FailedAttempts))
                    : TypedResults.NotFound())
            .WithName("GetProductProductionCost")
            .WithSummary(
                "What rendering for the Product is estimated to have cost so far, from the rates configured at each render: " +
                "every attempt, including those that failed and those whose Rendered Video has since been deleted. " +
                "An estimate, never an amount anyone was billed.");
    }

    private static ProductDetails ToDetails(this ProductRequest request) => new(
        request.Name,
        request.Category,
        request.Brand,
        request.Description,
        request.Price,
        request.Currency,
        request.OriginalUrl,
        request.AffiliateUrl,
        request.TargetAudience,
        request.Tags ?? []);

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Category,
        product.Brand,
        product.Description,
        product.Price,
        product.Currency,
        product.OriginalUrl,
        product.AffiliateUrl,
        product.TargetAudience,
        product.Tags,
        product.Status,
        product.CreatedAt,
        product.UpdatedAt);
}
