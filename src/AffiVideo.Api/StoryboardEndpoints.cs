using AffiVideo.Application;
using AffiVideo.Application.Storyboards;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class StoryboardEndpoints
{
    // The Storyboard versions of one of the caller's Organization's Variants. A
    // version is only ever added: there is no PUT, PATCH or DELETE.
    public static void MapStoryboards(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/projects/{projectId:guid}/variants/{variantId:guid}/storyboards").WithTags("Storyboards");

        group.MapGet("", async Task<Results<Ok<PagedResponse<StoryboardResponse>>, NotFound>> (
                Guid projectId, Guid variantId, int? page, int? pageSize, IStoryboards storyboards, CancellationToken cancellationToken) =>
                await storyboards.ListAsync(projectId, variantId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListStoryboards")
            .WithSummary("The Variant's Storyboard versions, newest first.");

        group.MapPost("", async Task<Results<Created<StoryboardResponse>, NotFound, ProblemHttpResult>> (
                Guid projectId, Guid variantId, IStoryboards storyboards, CancellationToken cancellationToken) =>
                await storyboards.GenerateAsync(projectId, variantId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Storyboard: { } made } => TypedResults.Created(
                        $"/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{made.Version}", ToResponse(made)),
                    var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("GenerateStoryboard")
            .WithSummary(
                "Plans the Variant's next Storyboard version in Product Lock from the Product's Confirmed Facts and photos, " +
                "with the mock planner and no AI. Answers 409 with the reason when it cannot: no Confirmed Fact, no usable photo, " +
                "a creative template other than Product Showcase, or a Hook, Fact or Product name too long for its layout.");

        group.MapGet("/{version:int}", async Task<Results<Ok<StoryboardResponse>, NotFound>> (
                Guid projectId, Guid variantId, int version, IStoryboards storyboards, CancellationToken cancellationToken) =>
                await storyboards.FindAsync(projectId, variantId, version, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetStoryboard")
            .WithSummary("One version of the Variant's Storyboard.");
    }

    private static StoryboardResponse ToResponse(Storyboard storyboard) => new(
        storyboard.Id,
        storyboard.VariantId,
        storyboard.Version,
        storyboard.CreativeTemplate,
        storyboard.TemplateVersion,
        storyboard.Planner,
        storyboard.RenderMode,
        storyboard.Scenes.Select(scene => new SceneResponse(
            scene.Position,
            scene.Layout,
            scene.Technique,
            scene.DurationMs,
            scene.OnScreenText,
            scene.NarrationText,
            scene.AssetIds,
            scene.Facts.Select(fact => new SceneFactResponse(fact.FactId, fact.Text)).ToList())).ToList(),
        storyboard.CreatedAt);
}
