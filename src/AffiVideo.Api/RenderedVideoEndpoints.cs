using System.Globalization;
using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace AffiVideo.Api;

internal static class RenderedVideoEndpoints
{
    // The library: the Rendered Videos of the caller's Organization. One of another
    // Organization is answered exactly like one that does not exist: 404. The MP4
    // leaves in two ways: to be previewed, in either state, and to be downloaded
    // as a file, once the Rendered Video is approved.
    public static void MapRenderedVideos(this IEndpointRouteBuilder routes)
    {
        var videos = routes.MapGroup("/rendered-videos").WithTags("Rendered Videos");

        videos.MapGet("", async Task<Results<Ok<PagedResponse<RenderedVideoResponse>>, ValidationProblem>> (
                string? search, RenderedVideoState? state, CreativeTemplate? creativeTemplate, RenderedVideoOrder? sort,
                int? page, int? pageSize, IRenderedVideos library, CancellationToken cancellationToken) =>
            {
                // A number is read as one of these too, and "7" is none of them.
                var refused = new Dictionary<string, string[]>();
                if (state is { } askedState && !Enum.IsDefined(askedState)) refused["state"] = ["Choose ReadyForReview or Approved."];
                if (creativeTemplate is { } askedTemplate && !Enum.IsDefined(askedTemplate))
                {
                    refused["creativeTemplate"] = [$"Choose one of {string.Join(", ", Enum.GetNames<CreativeTemplate>())}."];
                }
                if (sort is { } askedSort && !Enum.IsDefined(askedSort)) refused["sort"] = ["Choose NewestFirst or OldestFirst."];
                if (refused.Count > 0) return TypedResults.ValidationProblem(refused);

                var found = await library.ListAsync(
                    new RenderedVideoFilter(search, state, creativeTemplate, sort ?? RenderedVideoOrder.NewestFirst),
                    new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListRenderedVideos")
            .WithSummary(
                "The Organization's Rendered Videos, newest first unless sort says otherwise. Search looks in the " +
                "Product's name and the Project's objective; state and creative template narrow the list.");

        videos.MapGet("/{videoId:guid}", async Task<Results<Ok<RenderedVideoResponse>, NotFound>> (
                Guid videoId, IRenderedVideos library, CancellationToken cancellationToken) =>
                await library.FindAsync(videoId, cancellationToken) is { } video
                    ? TypedResults.Ok(ToResponse(video))
                    : TypedResults.NotFound())
            .WithName("GetRenderedVideo")
            .WithSummary("One Rendered Video.");

        videos.MapGet("/{videoId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid videoId, IRenderedVideos library, HttpContext context, CancellationToken cancellationToken) =>
            {
                var content = await library.OpenPreviewAsync(videoId, cancellationToken);
                if (content is null) return TypedResults.NotFound();

                // A player asks for the file in parts, and storage hands it over only from
                // the start, so it is read whole first. A Rendered Video is at most 30 seconds.
                var whole = new MemoryStream();
                await using (content)
                {
                    await content.CopyToAsync(whole, cancellationToken);
                }
                whole.Position = 0;

                AuthoriseEveryReading(context);
                return TypedResults.Stream(
                    whole, RenderedVideo.ContentType,
                    entityTag: new EntityTagHeaderValue($"\"{videoId:N}\""), enableRangeProcessing: true);
            })
            .Produces(StatusCodes.Status200OK, contentType: RenderedVideo.ContentType)
            .WithName("GetRenderedVideoContent")
            .WithSummary("The MP4, to preview in the browser. It needs no approval.");

        videos.MapGet("/{videoId:guid}/download", async Task<Results<FileStreamHttpResult, NotFound, ProblemHttpResult>> (
                Guid videoId, IRenderedVideos library, HttpContext context, CancellationToken cancellationToken) =>
            {
                var found = await library.DownloadAsync(videoId, cancellationToken);
                if (found is null) return TypedResults.NotFound();
                if (found.Content is null) return TypedResults.Problem(found.Refused, statusCode: StatusCodes.Status409Conflict);

                AuthoriseEveryReading(context);
                return TypedResults.Stream(found.Content, RenderedVideo.ContentType, fileDownloadName: FileName(found.Record));
            })
            .Produces(StatusCodes.Status200OK, contentType: RenderedVideo.ContentType)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("DownloadRenderedVideo")
            .WithSummary(
                "The same MP4 as the preview, to save as a file. Answers 409 until the Rendered Video is approved.");

        videos.MapPost("/{videoId:guid}/approve", async Task<Results<Ok<RenderedVideoResponse>, NotFound, ProblemHttpResult>> (
                Guid videoId, IRenderedVideos library, CancellationToken cancellationToken) =>
                await library.ApproveAsync(videoId, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } approved } => TypedResults.Ok(ToResponse(approved)),
                    var refused => TypedResults.Problem(refused.Refused, statusCode: StatusCodes.Status409Conflict),
                })
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("ApproveRenderedVideo")
            .WithSummary(
                "Approves a Rendered Video that is ready for review, in the member's name: it is fit to publish " +
                "and can be downloaded. Answers 409 for one that is already approved.");

        videos.MapDelete("/{videoId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid videoId, IRenderedVideos library, CancellationToken cancellationToken) =>
                await library.DeleteAsync(videoId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("DeleteRenderedVideo")
            .WithSummary(
                "Deletes a Rendered Video and its file, approved or not. Nothing brings it back. " +
                "The Storyboard version it was rendered from is kept and can be rendered again.");
    }

    // A browser may keep the file, but asks before using it again, so every
    // reading is authorised. A Rendered Video's content never changes.
    private static void AuthoriseEveryReading(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
    }

    // What the file is saved as: the Product and when it was rendered, so that two
    // videos of one Product do not land on the same name.
    private static string FileName(RenderedVideoRecord record)
    {
        var unusable = Path.GetInvalidFileNameChars().Concat("\\/:*?\"<>|").ToHashSet();
        var product = new string(record.ProductName.Select(letter => unusable.Contains(letter) ? '-' : letter).ToArray());
        return $"{product} {record.Video.CreatedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.mp4";
    }

    private static RenderedVideoResponse ToResponse(RenderedVideoRecord record) => new(
        record.Video.Id,
        record.Video.StoryboardId,
        record.Video.RenderJobId,
        record.Video.State,
        record.Video.DurationMs,
        record.Video.SizeInBytes,
        record.Video.UncutAssetIds,
        record.Video.CreatedAt,
        record.ProductId,
        record.ProductName,
        record.ProjectId,
        record.ProjectObjective,
        record.VariantId,
        record.CreativeTemplate,
        record.Hook,
        record.StoryboardVersion,
        record.Video.ApprovedByMemberId,
        record.ApprovedByEmail,
        record.Video.ApprovedAt);
}
