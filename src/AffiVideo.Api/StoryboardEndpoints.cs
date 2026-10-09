using AffiVideo.Application;
using AffiVideo.Application.Storyboards;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class StoryboardEndpoints
{
    // The Storyboard versions of one of the caller's Organization's Variants. A
    // version is only ever added: there is no PUT, PATCH or DELETE, and an edit
    // is a POST that makes the next version.
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
                ToResult(await storyboards.GenerateAsync(projectId, variantId, cancellationToken), projectId, variantId))
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

        group.MapPost("/{version:int}/edits", async Task<Results<Created<StoryboardResponse>, ValidationProblem, NotFound, ProblemHttpResult>> (
                Guid projectId, Guid variantId, int version, StoryboardEditRequest request,
                IStoryboards storyboards, CancellationToken cancellationToken) =>
            {
                if (request.Scenes.Any(scene => scene is null))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["scenes"] = ["A Scene is missing from the list."] });
                }

                var changes = request.Scenes
                    .Select(scene => new SceneChange(scene.Position, scene.OnScreenText, scene.NarrationText, scene.AssetId, scene.DurationMs))
                    .ToList();
                return await storyboards.EditAsync(projectId, variantId, version, changes, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Storyboard: { } made } => TypedResults.Created(
                        $"/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{made.Version}", ToResponse(made)),
                    var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                };
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("EditStoryboard")
            .WithSummary(
                "Makes the Variant's next Storyboard version from this one, edited: on-screen and narration text, the photo a Scene shows, " +
                "the order of the Scenes and their durations. This version stays as it is. A Scene whose text changes is marked " +
                "Manually Edited, and its text is no longer checked against Facts. Answers 409 with the reason when the edit changes " +
                "nothing, leaves a Scene out, breaks the target duration, shows an image the Product does not have, moves the Hook " +
                "from the start, or does not fit a layout.");

        group.MapPost("/{version:int}/scenes/{position:int}/regenerate", async Task<Results<Created<StoryboardResponse>, NotFound, ProblemHttpResult>> (
                Guid projectId, Guid variantId, int version, int position, IStoryboards storyboards, CancellationToken cancellationToken) =>
                ToResult(await storyboards.RegenerateSceneAsync(projectId, variantId, version, position, cancellationToken), projectId, variantId))
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RegenerateScene")
            .WithSummary(
                "Makes the Variant's next Storyboard version from this one with one Scene planned again, by the mock planner, from the " +
                "Product's Confirmed Facts and photos as they are now. The Scene keeps its place and its duration and is no longer " +
                "Manually Edited; every other Scene is as it was. Answers 409 with the reason when the Scene cannot be planned.");
    }

    private static Results<Created<StoryboardResponse>, NotFound, ProblemHttpResult> ToResult(
        StoryboardGeneration? made, Guid projectId, Guid variantId) => made switch
    {
        null => TypedResults.NotFound(),
        { Storyboard: { } storyboard } => TypedResults.Created(
            $"/api/v1/projects/{projectId}/variants/{variantId}/storyboards/{storyboard.Version}", ToResponse(storyboard)),
        var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
    };

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
            scene.Facts.Select(fact => new SceneFactResponse(fact.FactId, fact.Text)).ToList(),
            scene.ManuallyEdited)).ToList(),
        storyboard.CreatedAt);
}
