using AffiVideo.Application;
using AffiVideo.Application.Generation;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api;

internal static class GenerationEndpoints
{
    // Paid generation for the caller's Organization's Storyboards. Nothing here
    // generates: what a member confirms is queued for the worker (ADR 0003).
    public static void MapGeneration(this IEndpointRouteBuilder routes)
    {
        var ofStoryboard = routes
            .MapGroup("/projects/{projectId:guid}/variants/{variantId:guid}/storyboards/{version:int}/generation")
            .WithTags("Generation");

        ofStoryboard.MapGet("", async Task<Results<Ok<GenerationEstimateResponse>, NotFound>> (
                Guid projectId, Guid variantId, int version, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.EstimateAsync(projectId, variantId, version, cancellationToken) is { } estimate
                    ? TypedResults.Ok(ToResponse(estimate))
                    : TypedResults.NotFound())
            .WithName("EstimateGeneration")
            .WithSummary(
                "What generation for this Storyboard version would make, what that is estimated to cost, and what the Project has " +
                "spent against its ceiling. It spends nothing. With no image provider configured, it says generation is unavailable.");

        ofStoryboard.MapPost("", async Task<Results<Accepted<ConfirmedGenerationResponse>, Ok<ConfirmedGenerationResponse>, ValidationProblem, NotFound, ProblemHttpResult>> (
                Guid projectId, Guid variantId, int version, ConfirmGenerationRequest request,
                [FromHeader(Name = RenderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
                IGeneration generation, CancellationToken cancellationToken) =>
            {
                if (MissingKey(idempotencyKey) is { } missing) return missing;

                return await generation.ConfirmAsync(projectId, variantId, version, request.ConfirmedTotal, idempotencyKey!, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Refused: { } reason } => TypedResults.Problem(reason, statusCode: StatusCodes.Status409Conflict),
                    { Queued: true, Stills: var stills } => TypedResults.Accepted(
                        $"/api/v1/projects/{projectId}/stills", new ConfirmedGenerationResponse(stills.Select(ToResponse).ToList())),
                    { Stills: var already } => TypedResults.Ok(new ConfirmedGenerationResponse(already.Select(ToResponse).ToList())),
                };
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ConfirmGeneration")
            .WithSummary(
                "Confirms the estimate the member saw and queues its Stills for the worker. Nothing paid is generated without it. " +
                "Answers 409 with the reason when the confirmed total is not the estimate's, when there is nothing to generate, when " +
                "generation is unavailable, or when it would take the Project past its ceiling. A request repeated with the same " +
                "Idempotency-Key is answered 200 with the Stills the first one queued.");

        routes.MapGet("/projects/{projectId:guid}/stills", async Task<Results<Ok<PagedResponse<StillResponse>>, NotFound>> (
                Guid projectId, int? page, int? pageSize, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.ListStillsAsync(projectId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("ListStills")
            .WithSummary("The Stills made for the Project, newest first, rejected and failed ones included.");

        routes.MapGet("/stills/{stillId:guid}", async Task<Results<Ok<StillResponse>, NotFound>> (
                Guid stillId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.FindStillAsync(stillId, cancellationToken) is { } still
                    ? TypedResults.Ok(ToResponse(still))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("GetStill")
            .WithSummary("One Still and the state it is in.");

        routes.MapPost("/stills/{stillId:guid}/approve", async Task<Results<Ok<StillResponse>, NotFound, ProblemHttpResult>> (
                Guid stillId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.ApproveStillAsync(stillId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Still: { } approved } => TypedResults.Ok(ToResponse(approved)),
                    var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Generation")
            .WithName("ApproveStill")
            .WithSummary(
                "Records that the member accepts a Still that is ready for review, in the member's name. " +
                "Answers 409 with the reason for a Still in any other state.");

        routes.MapPost("/stills/{stillId:guid}/another", async Task<Results<Accepted<StillResponse>, Ok<StillResponse>, ValidationProblem, NotFound, ProblemHttpResult>> (
                Guid stillId, AnotherStillRequest request,
                [FromHeader(Name = RenderEndpoints.IdempotencyKeyHeader)] string? idempotencyKey,
                IGeneration generation, CancellationToken cancellationToken) =>
            {
                if (MissingKey(idempotencyKey) is { } missing) return missing;

                return await generation.AnotherStillAsync(stillId, request.ConfirmedAmount, idempotencyKey!, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Refused: { } reason } => TypedResults.Problem(reason, statusCode: StatusCodes.Status409Conflict),
                    { Queued: true, Still: { } queued } => TypedResults.Accepted($"/api/v1/stills/{queued.Still.Id}", ToResponse(queued)),
                    { Still: var already } => TypedResults.Ok(ToResponse(already!)),
                };
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Generation")
            .WithName("AskForAnotherStill")
            .WithSummary(
                "Rejects a Still that has been made and queues another of the same picture in its place. This is a new paid generation, " +
                "made only for the amount the member confirms, and it counts against the Project's ceiling. The rejected Still is kept, " +
                "with what it cost. Answers 409 with the reason for a Still that has not been made, when the confirmed amount is not what " +
                "another is estimated at now, or when it would take the Project past its ceiling. " +
                "A request repeated with the same Idempotency-Key is answered 200 with the Still the first one queued.");

        routes.MapGet("/stills/{stillId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid stillId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.OpenStillAsync(stillId, cancellationToken) is { } picture
                    ? TypedResults.Stream(picture, Still.ContentType)
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("GetStillContent")
            .WithSummary("The picture of a Still that has been made, as a PNG. Answers 404 until it is made.");
    }

    // Null when the request has a key that can be kept.
    private static ValidationProblem? MissingKey(string? idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > RenderJob.IdempotencyKeyMaxLength
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [RenderEndpoints.IdempotencyKeyHeader] =
                [
                    $"Send an {RenderEndpoints.IdempotencyKeyHeader} header of at most {RenderJob.IdempotencyKeyMaxLength} characters: a new value for each click.",
                ],
            })
            : null;

    private static GenerationEstimateResponse ToResponse(GenerationEstimate estimate) => new(
        estimate.Unavailable is null,
        estimate.Unavailable,
        estimate.Stills.Select(still => new EstimatedStillResponse(still.ScenePosition, still.Technique, still.Description, still.EstimatedAmount)).ToList(),
        estimate.Total,
        estimate.Currency,
        estimate.Spent,
        estimate.Ceiling,
        estimate.ImageProvider,
        estimate.SceneStills.Select(has => new SceneStillResponse(has.ScenePosition, ToResponse(has.Still))).ToList(),
        estimate.AnotherStillAmount);

    internal static StillResponse ToResponse(StillRecord record) => ToResponse(record.Still, record.Cost);

    private static StillResponse ToResponse(Still still, GenerationCostRecord? cost) => new(
        still.Id,
        still.ProjectId,
        still.Technique,
        still.Description,
        still.State,
        still.Provider,
        still.Model,
        still.Fake,
        still.EstimatedAmount,
        still.Currency,
        still.Attempt,
        still.RetryAt,
        still is { State: StillState.Failed, FailureKind: { } kind, FailureMessage: { } message }
            ? new StillFailureResponse(kind, message, still.Billed)
            : null,
        cost is null
            ? null
            : new StillCostResponse(cost.EstimatedAmount, cost.Currency, still.Provider, cost.Model, cost.RatesVersion, cost.RecordedAt),
        still.ApprovedByMemberId,
        still.ApprovedAt,
        still.CreatedAt,
        still.UpdatedAt);
}
