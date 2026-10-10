using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Application.Organizations;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class LabEndpoints
{
    /// <summary>
    /// The Affiliate Lab. Everything mapped on the group this returns answers 404 to a
    /// member of an Organization that does not have the Lab, exactly as if it were not there.
    /// </summary>
    public static RouteGroupBuilder MapLab(this IEndpointRouteBuilder routes) =>
        routes.MapGroup("/lab").WithMetadata(new RequiresAffiliateLab());

    /// <summary>Goes after the session is known and before anything of the request is read.</summary>
    public static void UseAffiliateLabGate(this WebApplication app) => app.Use(async (context, next) =>
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequiresAffiliateLab>() is not null)
        {
            // Asked of the database each time, not of the session, so the flag works from the moment it is set.
            var caller = context.RequestServices.GetRequiredService<Caller>();
            var organization = caller.OrganizationId is { } id
                ? await context.RequestServices.GetRequiredService<IOrganizations>().FindAsync(id, context.RequestAborted)
                : null;
            if (organization is not { AffiliateLabEnabled: true })
            {
                await TypedResults.NotFound().ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    });

    // What the Lab keeps about the Organization's own Products. A Product of another
    // Organization is answered exactly like one that does not exist: 404.
    public static void MapLabProducts(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/products").WithTags("Affiliate Lab");

        group.MapGet("", async (
                bool? shortlisted, int? page, int? pageSize, ILabProducts products, CancellationToken cancellationToken) =>
            {
                var found = await products.ListAsync(shortlisted, new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListLabProducts")
            .WithSummary("The Organization's Products with what the Affiliate Lab keeps about each, by name. `shortlisted=true` is the shortlist.");

        group.MapGet("/{productId:guid}", async Task<Results<Ok<LabProductResponse>, NotFound>> (
                Guid productId, ILabProducts products, CancellationToken cancellationToken) =>
                await products.FindAsync(productId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetLabProduct")
            .WithSummary("What the Affiliate Lab keeps about one Product.");

        group.MapPut("/{productId:guid}", async Task<Results<Ok<LabProductResponse>, ValidationProblem, NotFound>> (
                Guid productId, LabProductRequest request, ILabProducts products, CancellationToken cancellationToken) =>
            {
                var details = new LabProductDetails(
                    request.Shortlisted, request.ResearchNotes, request.CommissionRatePercent,
                    request.CommissionAmount, request.CommissionCurrency);
                return await products.ChangeAsync(productId, details, cancellationToken) is { } changed
                    ? TypedResults.Ok(ToResponse(changed))
                    : TypedResults.NotFound();
            })
            .WithName("UpdateLabProduct")
            .WithSummary(
                "Replaces what the Affiliate Lab keeps about a Product: whether it is shortlisted, the research notes, " +
                "and the commission as a rate or as an amount. Nothing else about the Product changes.");
    }

    // The Campaigns of the caller's Organization. A Campaign of another Organization,
    // Lab or no Lab, is answered exactly like one that does not exist: 404.
    public static void MapCampaigns(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/campaigns").WithTags("Affiliate Lab");

        group.MapGet("", async (
                CampaignStatus? status, int? page, int? pageSize, ICampaigns campaigns, CancellationToken cancellationToken) =>
            {
                var found = await campaigns.ListAsync(status, new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListCampaigns")
            .WithSummary("The Organization's Campaigns, newest first. A status narrows the list.");

        group.MapPost("", async Task<Results<Created<CampaignResponse>, ValidationProblem>> (
                CampaignRequest request, ICampaigns campaigns, CancellationToken cancellationToken) =>
            {
                var created = await campaigns.CreateAsync(request.Name, cancellationToken);
                return TypedResults.Created($"/api/v1/lab/campaigns/{created.Campaign.Id}", ToResponse(created));
            })
            .WithName("CreateCampaign")
            .WithSummary("Creates a Campaign with no Variants in it.");

        group.MapGet("/{campaignId:guid}", async Task<Results<Ok<CampaignResponse>, NotFound>> (
                Guid campaignId, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.FindAsync(campaignId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetCampaign")
            .WithSummary("One Campaign.");

        group.MapPut("/{campaignId:guid}", async Task<Results<Ok<CampaignResponse>, ValidationProblem, NotFound>> (
                Guid campaignId, CampaignRequest request, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.RenameAsync(campaignId, request.Name, cancellationToken) is { } renamed
                    ? TypedResults.Ok(ToResponse(renamed))
                    : TypedResults.NotFound())
            .WithName("RenameCampaign")
            .WithSummary("Renames a Campaign.");

        group.MapPost("/{campaignId:guid}/archive", async Task<Results<Ok<CampaignResponse>, NotFound>> (
                Guid campaignId, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.ArchiveAsync(campaignId, cancellationToken) is { } archived
                    ? TypedResults.Ok(ToResponse(archived))
                    : TypedResults.NotFound())
            .WithName("ArchiveCampaign")
            .WithSummary("Archives a Campaign. It keeps its Variants, and no Variant is deleted. Archiving one that is already archived changes nothing.");

        group.MapGet("/{campaignId:guid}/variants", async Task<Results<Ok<PagedResponse<CampaignVariantResponse>>, NotFound>> (
                Guid campaignId, int? page, int? pageSize, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.ListVariantsAsync(campaignId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListCampaignVariants")
            .WithSummary("The Variants the Campaign groups, in the order they were added, each with its Project and Product.");

        group.MapPut("/{campaignId:guid}/variants/{variantId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid campaignId, Guid variantId, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.AddVariantAsync(campaignId, variantId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("AddCampaignVariant")
            .WithSummary("Adds a Variant, of any Product, to the Campaign. Adding one that is already in it changes nothing.");

        group.MapDelete("/{campaignId:guid}/variants/{variantId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid campaignId, Guid variantId, ICampaigns campaigns, CancellationToken cancellationToken) =>
                await campaigns.RemoveVariantAsync(campaignId, variantId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("RemoveCampaignVariant")
            .WithSummary("Takes a Variant out of the Campaign. The Variant itself is kept.");
    }

    private static LabProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Brand,
        product.Category,
        product.Status,
        product.ShortlistedAt is not null,
        product.ResearchNotes,
        product.CommissionRatePercent,
        product.CommissionAmount,
        product.CommissionCurrency);

    private static CampaignResponse ToResponse(CampaignRecord record) => new(
        record.Campaign.Id,
        record.Campaign.Name,
        record.Campaign.Status,
        record.VariantCount,
        record.Campaign.CreatedAt,
        record.Campaign.UpdatedAt);

    private static CampaignVariantResponse ToResponse(CampaignVariantRecord record) => new(
        record.Variant.Id,
        record.Variant.ProjectId,
        record.ProductId,
        record.ProductName,
        record.Variant.CreativeTemplate,
        record.Variant.Hook,
        record.AddedAt);

    private sealed class RequiresAffiliateLab;
}
