using AffiVideo.Application;
using AffiVideo.Application.Products;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class FactEndpoints
{
    // The Facts of one of the caller's Organization's Products. As with the Product
    // itself, another Organization's is answered like one that does not exist: 404.
    // Nothing here changes a Fact's text: there is no PUT or PATCH.
    public static void MapFacts(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/products/{productId:guid}/facts").WithTags("Facts");

        group.MapGet("", async Task<Results<Ok<PagedResponse<FactResponse>>, ValidationProblem, NotFound>> (
                Guid productId, FactState? state, int? page, int? pageSize, IFacts facts, CancellationToken cancellationToken) =>
            {
                // A number is read as a state too, and "7" is no state at all.
                if (state is { } asked && !Enum.IsDefined(asked))
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["state"] = ["Choose Proposed, Confirmed or Withdrawn."] });
                }
                return await facts.ListAsync(productId, state, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound();
            })
            .WithName("ListFacts")
            .WithSummary("The Product's Facts, oldest first. State narrows the list to Proposed, Confirmed or Withdrawn.");

        group.MapPost("", async Task<Results<Created<FactResponse>, ValidationProblem, NotFound>> (
                Guid productId, FactRequest request, IFacts facts, CancellationToken cancellationToken) =>
                await facts.AddAsync(productId, request.ToDetails(), cancellationToken) is { } added
                    ? Created(added)
                    : TypedResults.NotFound())
            .WithName("AddFact")
            .WithSummary("Adds a Fact to the Product. It starts as Proposed.");

        group.MapGet("/{factId:guid}", async Task<Results<Ok<FactResponse>, NotFound>> (
                Guid productId, Guid factId, IFacts facts, CancellationToken cancellationToken) =>
                await facts.FindAsync(productId, factId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetFact")
            .WithSummary("One Fact.");

        group.MapPost("/{factId:guid}/confirm", async Task<Results<Ok<FactResponse>, NotFound, ProblemHttpResult>> (
                Guid productId, Guid factId, IFacts facts, CancellationToken cancellationToken) =>
                await facts.ConfirmAsync(productId, factId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } confirmed } => TypedResults.Ok(ToResponse(confirmed)),
                    var refused => Conflict(refused),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ConfirmFact")
            .WithSummary("Confirms a Proposed Fact in the member's name. Answers 409 for a Fact in any other state.");

        group.MapPost("/{factId:guid}/withdraw", async Task<Results<Ok<FactResponse>, NotFound, ProblemHttpResult>> (
                Guid productId, Guid factId, IFacts facts, CancellationToken cancellationToken) =>
                await facts.WithdrawAsync(productId, factId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } withdrawn } => TypedResults.Ok(ToResponse(withdrawn)),
                    var refused => Conflict(refused),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("WithdrawFact")
            .WithSummary("Withdraws a Proposed or Confirmed Fact. Answers 409 for one that is already Withdrawn.");

        group.MapPost("/{factId:guid}/replace", async Task<Results<Created<FactResponse>, ValidationProblem, NotFound, ProblemHttpResult>> (
                Guid productId, Guid factId, FactRequest request, IFacts facts, CancellationToken cancellationToken) =>
                await facts.ReplaceAsync(productId, factId, request.ToDetails(), cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } replacement } => Created(replacement),
                    var refused => Conflict(refused),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ReplaceFact")
            .WithSummary(
                "How a Fact is edited: withdraws it and adds a new Proposed Fact, which is the answer. " +
                "Answers 409 for a Fact that is already Withdrawn.");
    }

    private static Created<FactResponse> Created(FactRecord record) =>
        TypedResults.Created($"/api/v1/products/{record.Fact.ProductId}/facts/{record.Fact.Id}", ToResponse(record));

    private static ProblemHttpResult Conflict(FactChange refused) =>
        TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict);

    private static FactDetails ToDetails(this FactRequest request) => new(request.Text, request.Language, request.Source);

    private static FactResponse ToResponse(FactRecord record) => new(
        record.Fact.Id,
        record.Fact.ProductId,
        record.Fact.Text,
        record.Fact.Language,
        record.Fact.Source,
        record.Fact.State,
        record.Fact.CreatedAt,
        record.Fact.ConfirmedByMemberId,
        record.ConfirmedByEmail,
        record.Fact.ConfirmedAt,
        record.Fact.WithdrawnAt);
}
