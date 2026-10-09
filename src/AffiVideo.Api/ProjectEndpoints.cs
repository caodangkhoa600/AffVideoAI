using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class ProjectEndpoints
{
    // These are the Projects of the caller's Organization. A Project of another
    // Organization is answered exactly like one that does not exist: 404.
    // Nothing here changes a Project's brief: there is no PUT or PATCH.
    public static void MapProjects(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/projects").WithTags("Projects");

        group.MapGet("", async (
                Guid? productId, int? page, int? pageSize, IProjects projects, CancellationToken cancellationToken) =>
            {
                var found = await projects.ListAsync(productId, new PageRequest(page, pageSize), cancellationToken);
                return TypedResults.Ok(found.ToResponse(ToResponse));
            })
            .WithName("ListProjects")
            .WithSummary("The Organization's Projects, newest first. A Product narrows the list to the Projects made from it.");

        group.MapPost("", async Task<Results<Created<ProjectResponse>, ValidationProblem>> (
                ProjectRequest request, IProjects projects, CancellationToken cancellationToken) =>
            {
                var brief = new ProjectBrief(request.Audience, request.Language, request.TargetDurationSeconds, request.Objective);
                return await projects.CreateAsync(request.ProductId, brief, cancellationToken) is { } created
                    ? TypedResults.Created($"/api/v1/projects/{created.Project.Id}", ToResponse(created))
                    : TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["productId"] = ["There is no such Product in your Organization."] });
            })
            .WithName("CreateProject")
            .WithSummary("Creates a Project from one Product, with its brief. The brief cannot be changed afterwards.");

        group.MapGet("/{projectId:guid}", async Task<Results<Ok<ProjectResponse>, NotFound>> (
                Guid projectId, IProjects projects, CancellationToken cancellationToken) =>
                await projects.FindAsync(projectId, cancellationToken) is { } found
                    ? TypedResults.Ok(ToResponse(found))
                    : TypedResults.NotFound())
            .WithName("GetProject")
            .WithSummary("One Project.");

        group.MapDelete("/{projectId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid projectId, IProjects projects, CancellationToken cancellationToken) =>
                await projects.DeleteAsync(projectId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("DeleteProject")
            .WithSummary("Deletes a Project and its Variants. The Product is kept.");
    }

    private static ProjectResponse ToResponse(ProjectRecord record) => new(
        record.Project.Id,
        record.Project.ProductId,
        record.ProductName,
        record.Project.Audience,
        record.Project.Language,
        record.Project.TargetDurationSeconds,
        record.Project.Objective,
        record.VariantCount,
        record.Project.CreatedAt);
}
