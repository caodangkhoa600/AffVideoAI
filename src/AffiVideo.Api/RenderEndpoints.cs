using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace AffiVideo.Api;

internal static class RenderEndpoints
{
    // Render jobs and Rendered Videos of the caller's Organization. Nothing here
    // renders: a job is queued for the worker, and the web app asks for its state.
    public static void MapRenders(this IEndpointRouteBuilder routes)
    {
        var ofStoryboard = routes
            .MapGroup("/projects/{projectId:guid}/variants/{variantId:guid}/storyboards/{version:int}/renders")
            .WithTags("Rendering");

        ofStoryboard.MapPost("", async Task<Results<Accepted<RenderJobResponse>, NotFound>> (
                Guid projectId, Guid variantId, int version, IRenders renders, CancellationToken cancellationToken) =>
                await renders.SubmitAsync(projectId, variantId, version, cancellationToken) is { } job
                    ? TypedResults.Accepted($"/api/v1/render-jobs/{job.Id}", ToResponse(job))
                    : TypedResults.NotFound())
            .WithName("SubmitRender")
            .WithSummary(
                "Queues a job that renders this Storyboard version in Product Lock. The worker renders it; " +
                "ask for the job to see its state.");

        ofStoryboard.MapGet("", async Task<Results<Ok<PagedResponse<RenderJobResponse>>, NotFound>> (
                Guid projectId, Guid variantId, int version, int? page, int? pageSize,
                IRenders renders, CancellationToken cancellationToken) =>
                await renders.ListJobsAsync(projectId, variantId, version, new PageRequest(page, pageSize), cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListRenderJobs")
            .WithSummary("The jobs that render this Storyboard version, newest first.");

        routes.MapGet("/render-jobs/{jobId:guid}", async Task<Results<Ok<RenderJobResponse>, NotFound>> (
                Guid jobId, IRenders renders, CancellationToken cancellationToken) =>
                await renders.FindJobAsync(jobId, cancellationToken) is { } job
                    ? TypedResults.Ok(ToResponse(job))
                    : TypedResults.NotFound())
            .WithTags("Rendering")
            .WithName("GetRenderJob")
            .WithSummary("One render job and the state it is in.");

        var videos = routes.MapGroup("/rendered-videos").WithTags("Rendered Videos");

        videos.MapGet("/{videoId:guid}", async Task<Results<Ok<RenderedVideoResponse>, NotFound>> (
                Guid videoId, IRenders renders, CancellationToken cancellationToken) =>
                await renders.FindVideoAsync(videoId, cancellationToken) is { } video
                    ? TypedResults.Ok(ToResponse(video))
                    : TypedResults.NotFound())
            .WithName("GetRenderedVideo")
            .WithSummary("One Rendered Video.");

        videos.MapGet("/{videoId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid videoId, IRenders renders, HttpContext context, CancellationToken cancellationToken) =>
            {
                var found = await renders.OpenVideoAsync(videoId, cancellationToken);
                if (found is null) return TypedResults.NotFound();

                // A player asks for the file in parts, and storage hands it over only from
                // the start, so it is read whole first. A Rendered Video is at most 30 seconds.
                var whole = new MemoryStream();
                await using (found.Content)
                {
                    await found.Content.CopyToAsync(whole, cancellationToken);
                }
                whole.Position = 0;

                // A browser may keep the video, but asks before playing it again, so
                // every playing is authorised. A Rendered Video's content never changes.
                context.Response.Headers.CacheControl = "private, no-cache";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.Stream(
                    whole, RenderedVideo.ContentType,
                    entityTag: new EntityTagHeaderValue($"\"{found.Video.Id:N}\""), enableRangeProcessing: true);
            })
            .Produces(StatusCodes.Status200OK, contentType: RenderedVideo.ContentType)
            .WithName("GetRenderedVideoContent")
            .WithSummary("The MP4, to preview in the browser.");
    }

    private static RenderJobResponse ToResponse(RenderJob job) => new(
        job.Id,
        job.StoryboardId,
        job.State,
        job.FailureReason,
        job.RenderedVideoId,
        job.CreatedAt,
        job.UpdatedAt);

    private static RenderedVideoResponse ToResponse(RenderedVideo video) => new(
        video.Id,
        video.StoryboardId,
        video.RenderJobId,
        video.State,
        video.DurationMs,
        video.SizeInBytes,
        video.UncutAssetIds,
        video.CreatedAt);
}
