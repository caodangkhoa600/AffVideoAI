using AffiVideo.Application.SystemStatus;
using AffiVideo.Contracts;

namespace AffiVideo.Api;

internal static class StatusEndpoints
{
    public static void MapStatus(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/status", async (ISystemStatusReader reader, CancellationToken cancellationToken) =>
            {
                var status = await reader.ReadAsync(cancellationToken);
                return TypedResults.Ok(new StatusResponse(
                    new DependencyStatusResponse(status.DatabaseReachable),
                    new DependencyStatusResponse(status.ObjectStorageReachable)));
            })
            .WithName("GetStatus")
            .WithSummary("Whether the API can reach its database and object storage.")
            .WithTags("Status");
    }
}
