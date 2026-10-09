using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class VariantEndpoints
{
    // The Variants of one of the caller's Organization's Projects. Nothing here
    // changes or removes a Variant, so its identifier is its identifier for life.
    public static void MapVariants(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/projects/{projectId:guid}/variants").WithTags("Variants");

        group.MapGet("", async Task<Results<Ok<PagedResponse<VariantResponse>>, NotFound>> (
                Guid projectId, int? page, int? pageSize, IVariants variants, CancellationToken cancellationToken) =>
                await variants.ListAsync(projectId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListVariants")
            .WithSummary("The Project's Variants, oldest first.");

        group.MapPost("", async Task<Results<Created<VariantResponse>, ValidationProblem, NotFound>> (
                Guid projectId, VariantRequest request, IVariants variants, CancellationToken cancellationToken) =>
                await variants.AddAsync(projectId, request.CreativeTemplate, request.Hook, cancellationToken) is { } added
                    ? Created(added)
                    : TypedResults.NotFound())
            .WithName("AddVariant")
            .WithSummary("Adds a Variant to the Project: a creative template and a Hook.");

        group.MapGet("/{variantId:guid}", async Task<Results<Ok<VariantResponse>, NotFound>> (
                Guid projectId, Guid variantId, IVariants variants, CancellationToken cancellationToken) =>
                await variants.FindAsync(projectId, variantId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetVariant")
            .WithSummary("One Variant.");

        group.MapPost("/{variantId:guid}/duplicate", async Task<Results<Created<VariantResponse>, ValidationProblem, NotFound>> (
                Guid projectId, Guid variantId, DuplicateVariantRequest request, IVariants variants, CancellationToken cancellationToken) =>
                await variants.DuplicateAsync(projectId, variantId, request.Hook, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Variant: { } duplicate } => Created(duplicate),
                    var refused => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["hook"] = [refused.Refused!] }),
                })
            .WithName("DuplicateVariant")
            .WithSummary(
                "Adds a separate Variant with the same creative template and a new Hook, which is the answer. " +
                "The Variant that was duplicated is not changed.");
    }

    private static Created<VariantResponse> Created(Variant variant) =>
        TypedResults.Created($"/api/v1/projects/{variant.ProjectId}/variants/{variant.Id}", ToResponse(variant));

    private static VariantResponse ToResponse(Variant variant) => new(
        variant.Id,
        variant.ProjectId,
        variant.CreativeTemplate,
        variant.Hook,
        variant.CreatedAt);
}
