using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

// Part of the Affiliate Lab: all of it is mapped on the group MapLab returns.
internal static class PublishedPostEndpoints
{
    // The social accounts of the caller's Organization, kept to be chosen again.
    public static void MapSocialAccounts(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/social-accounts").WithTags("Affiliate Lab");

        group.MapGet("", async (int? page, int? pageSize, ISocialAccounts accounts, CancellationToken cancellationToken) =>
            {
                var found = await accounts.ListAsync(new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListSocialAccounts")
            .WithSummary("The Organization's social accounts, by platform and then by handle.");

        group.MapPost("", async Task<Results<Created<SocialAccountResponse>, ValidationProblem, ProblemHttpResult>> (
                SocialAccountRequest request, ISocialAccounts accounts, CancellationToken cancellationToken) =>
                await accounts.CreateAsync(request.Platform, request.Handle, cancellationToken) is { } created
                    ? TypedResults.Created((string?)null, ToResponse(created))
                    : TypedResults.Problem("This social account is already recorded.", statusCode: StatusCodes.Status409Conflict))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateSocialAccount")
            .WithSummary("Records a social account: a platform and a handle on it. Answers 409 for one that is already recorded.");
    }

    // The affiliate links of the caller's Organization, kept to be chosen again.
    public static void MapAffiliateLinks(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/affiliate-links").WithTags("Affiliate Lab");

        group.MapGet("", async (int? page, int? pageSize, IAffiliateLinks links, CancellationToken cancellationToken) =>
            {
                var found = await links.ListAsync(new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListAffiliateLinks")
            .WithSummary("The Organization's affiliate links, newest first, each with how many Published Posts carry it.");

        group.MapPost("", async Task<Results<Created<AffiliateLinkResponse>, ValidationProblem, ProblemHttpResult>> (
                AffiliateLinkRequest request, IAffiliateLinks links, CancellationToken cancellationToken) =>
                await links.CreateAsync(request.Url, request.Label, cancellationToken) is { } created
                    ? TypedResults.Created((string?)null, ToResponse(created))
                    : TypedResults.Problem("This affiliate link is already recorded.", statusCode: StatusCodes.Status409Conflict))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("CreateAffiliateLink")
            .WithSummary("Records an affiliate link. The address is only kept, never requested. Answers 409 for one that is already recorded.");
    }

    // The Published Posts of the caller's Organization. One of another Organization
    // is answered exactly like one that does not exist: 404. Nothing here posts anything.
    public static void MapPublishedPosts(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/published-posts").WithTags("Affiliate Lab");

        group.MapGet("", async Task<Results<Ok<PagedResponse<PublishedPostResponse>>, ValidationProblem>> (
                Guid? campaignId, Guid? productId, Guid? variantId, SocialPlatform? platform, Guid? socialAccountId,
                int? page, int? pageSize, IPublishedPosts posts, CancellationToken cancellationToken) =>
            {
                // A number is read as a platform too, and "7" is none of them.
                if (platform is { } asked && !Enum.IsDefined(asked))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["platform"] = [$"Choose one of {string.Join(", ", Enum.GetNames<SocialPlatform>())}."],
                    });
                }

                var found = await posts.ListAsync(
                    new PublishedPostFilter(campaignId, productId, variantId, platform, socialAccountId),
                    new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListPublishedPosts")
            .WithSummary(
                "The Organization's Published Posts, the latest publication date first. A Campaign, a Product, " +
                "a Variant, a platform and a social account each narrow the list.");

        group.MapPost("", async Task<Results<Created<PublishedPostResponse>, ValidationProblem, ProblemHttpResult>> (
                PublishedPostRequest request, IPublishedPosts posts, CancellationToken cancellationToken) =>
                await posts.RecordAsync(
                        new NewPublishedPost(
                            request.RenderedVideoId, request.SocialAccountId, request.AffiliateLinkId, request.PublishedOn, request.Url),
                        cancellationToken) switch
                    {
                        { Record: { } recorded } =>
                            TypedResults.Created($"/api/v1/lab/published-posts/{recorded.Post.Id}", ToResponse(recorded)),
                        { Unknown: { } unknown } =>
                            TypedResults.ValidationProblem(unknown.ToDictionary(field => field.Key, field => new[] { field.Value })),
                        var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                    })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RecordPublishedPost")
            .WithSummary(
                "Records that an approved Rendered Video was posted, by hand, on a social account at a URL, with the " +
                "affiliate link it carries if any. Nothing is posted. Answers 409 for a Rendered Video that is not " +
                "approved, and for a URL that is already recorded.");

        group.MapGet("/{postId:guid}", async Task<Results<Ok<PublishedPostResponse>, NotFound>> (
                Guid postId, IPublishedPosts posts, CancellationToken cancellationToken) =>
                await posts.FindAsync(postId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetPublishedPost")
            .WithSummary("One Published Post.");
    }

    private static PublishedPostResponse ToResponse(PublishedPostRecord record) => new(
        record.Post.Id,
        record.Post.RenderedVideoId,
        record.ProductId,
        record.ProductName,
        record.ProjectId,
        record.VariantId,
        record.CreativeTemplate,
        record.Hook,
        ToResponse(record.Account),
        record.Post.PublishedOn,
        record.Post.Url,
        record.AffiliateLink is { } link ? new PublishedPostLinkResponse(link.Id, link.Url, link.Label) : null,
        record.AffiliateLinkShared,
        record.Current is { } current ? PerformanceSnapshotEndpoints.ToCurrent(current) : null,
        record.Commission?.Select(CommissionEndpoints.ToResponse).ToList(),
        record.Post.CreatedAt);

    private static SocialAccountResponse ToResponse(SocialAccount account) =>
        new(account.Id, account.Platform, account.Handle, account.CreatedAt);

    private static AffiliateLinkResponse ToResponse(AffiliateLinkRecord record) =>
        new(record.Link.Id, record.Link.Url, record.Link.Label, record.PublishedPostCount, record.Link.CreatedAt);
}
