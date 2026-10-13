using AffiVideo.Application;
using AffiVideo.Application.Generation;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

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
                "What generation for this Storyboard version would make, Stills and Clips, what that is estimated to cost, and what the " +
                "Project has spent against its ceiling. It spends nothing. With no image provider configured, it says generation is unavailable.");

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
                    { Queued: true } queued => TypedResults.Accepted($"/api/v1/projects/{projectId}/stills", ToResponse(queued)),
                    var already => TypedResults.Ok(ToResponse(already)),
                };
            })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ConfirmGeneration")
            .WithSummary(
                "Confirms the estimate the member saw and queues its Stills for the worker. The one confirmation covers the Clips of the " +
                "estimate too: each is made from its Still once a member has approved that. Nothing paid is generated without it. " +
                "Answers 409 with the reason when the confirmed total is not the estimate's, when there is nothing to generate, when " +
                "generation is unavailable, or when it would take the Project past its ceiling. A request repeated with the same " +
                "Idempotency-Key is answered 200 with what the first one queued.");

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
                "Records that the member accepts a Still that is ready for review, in the member's name. A Clip that was confirmed " +
                "with it is then queued, to be made from it. Answers 409 with the reason for a Still in any other state.");

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
                "another is estimated at now, when a Clip has been made from it, or when it would take the Project past its ceiling. " +
                "A request repeated with the same Idempotency-Key is answered 200 with the Still the first one queued.");

        routes.MapGet("/stills/{stillId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid stillId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.OpenStillAsync(stillId, cancellationToken) is { } picture
                    ? TypedResults.Stream(picture, Still.ContentType)
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("GetStillContent")
            .WithSummary("The picture of a Still that has been made, as a PNG. Answers 404 until it is made.");

        routes.MapGet("/projects/{projectId:guid}/clips", async Task<Results<Ok<PagedResponse<ClipResponse>>, NotFound>> (
                Guid projectId, int? page, int? pageSize, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.ListClipsAsync(projectId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("ListClips")
            .WithSummary("The Clips made for the Project, newest first, failed ones and those that wait for their Still included.");

        routes.MapGet("/clips/{clipId:guid}", async Task<Results<Ok<ClipResponse>, NotFound>> (
                Guid clipId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.FindClipAsync(clipId, cancellationToken) is { } clip
                    ? TypedResults.Ok(ToResponse(clip))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("GetClip")
            .WithSummary("One Clip and the state it is in.");

        routes.MapPost("/clips/{clipId:guid}/approve", async Task<Results<Ok<ClipResponse>, NotFound, ProblemHttpResult>> (
                Guid clipId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.ApproveClipAsync(clipId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Clip: { } approved } => TypedResults.Ok(ToResponse(approved)),
                    var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Generation")
            .WithName("ApproveClip")
            .WithSummary(
                "Records that the member accepts a Clip that is ready for review, having watched it, in the member's name. It makes the " +
                "next Storyboard version of each Variant of the Project whose Scene describes that footage, and that version refers to " +
                "the Clip. Answers 409 with the reason for a Clip in any other state.");

        routes.MapGet("/clips/{clipId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid clipId, IGeneration generation, HttpContext context, CancellationToken cancellationToken) =>
            {
                var content = await generation.OpenClipAsync(clipId, cancellationToken);
                if (content is null) return TypedResults.NotFound();

                // As for a Rendered Video: a player asks for the file in parts, and storage hands it
                // over only from the start, so it is read whole first. A Clip is as long as one Scene.
                var whole = new MemoryStream();
                await using (content)
                {
                    await content.CopyToAsync(whole, cancellationToken);
                }
                whole.Position = 0;

                RenderedVideoEndpoints.AuthoriseEveryReading(context);
                return TypedResults.Stream(
                    whole, Clip.ContentType, entityTag: new EntityTagHeaderValue($"\"{clipId:N}\""), enableRangeProcessing: true);
            })
            .Produces(StatusCodes.Status200OK, contentType: Clip.ContentType)
            .WithTags("Generation")
            .WithName("GetClipContent")
            .WithSummary("The video of a Clip that has been made, as an MP4 with no sound, to watch before rendering. Answers 404 until it is made.");

        routes.MapGet("/projects/{projectId:guid}/speech", async Task<Results<Ok<PagedResponse<SpeechResponse>>, NotFound>> (
                Guid projectId, int? page, int? pageSize, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.ListSpeechAsync(projectId, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("ListSpeech")
            .WithSummary("The speech made for the Project, newest first, failed pieces included.");

        routes.MapGet("/speech/{speechId:guid}", async Task<Results<Ok<SpeechResponse>, NotFound>> (
                Guid speechId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.FindSpeechAsync(speechId, cancellationToken) is { } speech
                    ? TypedResults.Ok(ToResponse(speech))
                    : TypedResults.NotFound())
            .WithTags("Generation")
            .WithName("GetSpeech")
            .WithSummary("One piece of speech and the state it is in.");

        routes.MapGet("/speech/{speechId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid speechId, IGeneration generation, CancellationToken cancellationToken) =>
                await generation.OpenSpeechAsync(speechId, cancellationToken) is { } sound
                    ? TypedResults.Stream(sound, Speech.ContentType)
                    : TypedResults.NotFound())
            .Produces(StatusCodes.Status200OK, contentType: Speech.ContentType)
            .WithTags("Generation")
            .WithName("GetSpeechContent")
            .WithSummary("The sound of speech that has been made, as a WAV, to listen to before rendering. Answers 404 until it is made.");
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
        estimate.Clips.Select(clip => new EstimatedClipResponse(clip.ScenePosition, clip.Description, clip.DurationMs, clip.EstimatedAmount)).ToList(),
        estimate.Speech.Select(speech => new EstimatedSpeechResponse(
            speech.ScenePosition, speech.NarrationText, speech.EstimatedAmount, speech.EstimatedDurationMs, speech.Warning)).ToList(),
        estimate.Total,
        estimate.Currency,
        estimate.Spent,
        estimate.Ceiling,
        estimate.ImageProvider,
        estimate.VideoProvider,
        estimate.SpeechProvider,
        estimate.Voice,
        estimate.SceneStills.Select(has => new SceneStillResponse(has.ScenePosition, ToResponse(has.Still))).ToList(),
        estimate.SceneClips.Select(has => new SceneClipResponse(has.ScenePosition, ToResponse(has.Clip))).ToList(),
        estimate.SceneSpeech.Select(has => new SceneSpeechResponse(has.ScenePosition, ToResponse(has.Speech))).ToList(),
        estimate.AnotherStillAmount);

    private static ConfirmedGenerationResponse ToResponse(GenerationConfirmation confirmation) => new(
        confirmation.Stills.Select(ToResponse).ToList(), confirmation.Clips.Select(ToResponse).ToList(),
        confirmation.Speech.Select(ToResponse).ToList());

    private static SpeechResponse ToResponse(SpeechRecord record)
    {
        var (speech, cost) = record;
        return new(
            speech.Id,
            speech.ProjectId,
            speech.Text,
            speech.Language,
            speech.Voice,
            speech.State,
            speech.Provider,
            speech.Model,
            speech.Fake,
            speech.DurationMs,
            speech.EstimatedAmount,
            speech.Currency,
            speech.Attempt,
            speech.RetryAt,
            speech is { State: SpeechState.Failed, FailureKind: { } kind, FailureMessage: { } message }
                ? new SpeechFailureResponse(kind, message, speech.Billed)
                : null,
            cost is null
                ? null
                : new SpeechCostResponse(cost.EstimatedAmount, cost.Currency, speech.Provider, cost.Model, cost.RatesVersion, cost.RecordedAt),
            speech.CreatedAt,
            speech.UpdatedAt);
    }

    private static ClipResponse ToResponse(ClipRecord record)
    {
        var (clip, cost) = record;
        return new(
            clip.Id,
            clip.ProjectId,
            clip.StillId,
            clip.Description,
            clip.DurationMs,
            clip.State,
            clip.Provider,
            clip.Model,
            clip.Fake,
            clip.EstimatedAmount,
            clip.Currency,
            clip.Attempt,
            clip.RetryAt,
            clip is { State: ClipState.Failed, FailureKind: { } kind, FailureMessage: { } message }
                ? new ClipFailureResponse(kind, message, clip.Billed)
                : null,
            cost is null
                ? null
                : new ClipCostResponse(cost.EstimatedAmount, cost.Currency, clip.Provider, cost.Model, cost.RatesVersion, cost.RecordedAt),
            clip.ApprovedByMemberId,
            clip.ApprovedAt,
            clip.CreatedAt,
            clip.UpdatedAt);
    }

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
