using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

// Part of the Affiliate Lab: all of it is mapped on the group MapLab returns.
internal static class CommissionEndpoints
{
    // The Commission records of the caller's Organization, and what they add up to for an
    // affiliate link and for a Product. Anything of another Organization is answered exactly
    // like something that does not exist. Nothing here divides Commission between Published Posts.
    public static void MapCommission(this RouteGroupBuilder lab)
    {
        var group = lab.MapGroup("/commission-records").WithTags("Affiliate Lab");

        group.MapGet("", async (
                Guid? affiliateLinkId, Guid? productId, int? page, int? pageSize, ICommissionRecords commission,
                CancellationToken cancellationToken) =>
            {
                var found = await commission.ListAsync(
                    new CommissionRecordFilter(affiliateLinkId, productId), new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListCommissionRecords")
            .WithSummary(
                "The Organization's Commission records, the latest period first. An affiliate link or a Product " +
                "narrows the list to the records attached to it. Each says whether it overlaps another.");

        group.MapPost("", async Task<Results<Created<CommissionRecordResponse>, ValidationProblem, ProblemHttpResult>> (
                CommissionRecordRequest request, ICommissionRecords commission, CancellationToken cancellationToken) =>
                await commission.RecordManualAsync(
                        new NewCommissionRecord(
                            new CommissionTarget(request.AffiliateLinkId, request.ProductId),
                            request.PeriodStart, request.PeriodEnd, request.Report, request.Currency,
                            new CommissionFigures(
                                request.Orders, request.ConfirmedOrders, request.Commission, request.Refunds, request.Adjustments)),
                        cancellationToken) switch
                    {
                        { Record: { } recorded } => TypedResults.Created((string?)null, ToResponse(recorded)),
                        { NoSuch: { } missing } =>
                            TypedResults.ValidationProblem(missing.ToDictionary(field => field.Key, field => new[] { field.Value })),
                        var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                    })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("RecordCommission")
            .WithSummary(
                "Records what an affiliate report says for a period, as manual entry, attached to one affiliate link or " +
                "one Product: orders, confirmed orders, Commission, the refunds that reduce it and the adjustments that " +
                "reduce or add to it. Answers 409 for a record that is already there for the same report, currency and " +
                "period. A period that overlaps another's is accepted, and the answer says so.");

        group.MapDelete("/{recordId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid recordId, ICommissionRecords commission, CancellationToken cancellationToken) =>
                await commission.DeleteAsync(recordId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("DeleteCommissionRecord")
            .WithSummary(
                "Deletes a Commission record, which is how a wrong one is corrected: it is deleted and recorded " +
                "again. The deletion is written to the audit log.");

        lab.MapGet("/affiliate-links/{linkId:guid}/commission", async Task<Results<Ok<LinkCommissionResponse>, NotFound>> (
                Guid linkId, ICommissionRecords commission, CancellationToken cancellationToken) =>
                await commission.ForLinkAsync(linkId, cancellationToken) is { } found
                    ? TypedResults.Ok(new LinkCommissionResponse(
                        found.Link.Id, found.Link.Url, found.Link.Label, found.PublishedPostCount, found.ProductIds,
                        ToResponse(found.Totals)))
                    : TypedResults.NotFound())
            .WithTags("Affiliate Lab")
            .WithName("GetAffiliateLinkCommission")
            .WithSummary(
                "What is recorded for an affiliate link, one total for each currency, with how many Published Posts " +
                "carry the link and which Products they are of. When more than one carries it, the totals cannot be " +
                "split between them.");

        lab.MapGet("/products/{productId:guid}/commission", async Task<Results<Ok<ProductCommissionResponse>, NotFound>> (
                Guid productId, ICommissionRecords commission, CancellationToken cancellationToken) =>
                await commission.ForProductAsync(productId, cancellationToken) is { } found
                    ? TypedResults.Ok(new ProductCommissionResponse(
                        found.ProductId, found.ProductName, ToResponse(found.RecordedForProduct), ToResponse(found.RecordedForLinks)))
                    : TypedResults.NotFound())
            .WithTags("Affiliate Lab")
            .WithName("GetProductCommission")
            .WithSummary(
                "What is recorded for a Product, and apart from it what is recorded for the affiliate links that " +
                "only Published Posts of this Product carry. The two are never added to each other.");
    }

    public static List<CommissionTotalResponse> ToResponse(IReadOnlyList<CommissionTotal> totals) =>
        totals
            .Select(total => new CommissionTotalResponse(
                total.Currency, total.Orders, total.ConfirmedOrders, total.Commission, total.Refunds, total.Adjustments, total.Net,
                total.Records, total.OverlappingRecords, total.Reports, total.Sources, total.PeriodStart, total.PeriodEnd))
            .ToList();

    private static CommissionRecordResponse ToResponse(CommissionRecordEntry entry) => new(
        entry.Record.Id,
        entry.Record.AffiliateLinkId,
        entry.Record.ProductId,
        entry.Record.PeriodStart,
        entry.Record.PeriodEnd,
        entry.Record.Report,
        entry.Record.Currency,
        entry.Record.Orders,
        entry.Record.ConfirmedOrders,
        entry.Record.Commission,
        entry.Record.Refunds,
        entry.Record.Adjustments,
        entry.Record.Net,
        entry.Record.Source,
        entry.Record.RecordedAt,
        entry.OverlapsAnother);
}
