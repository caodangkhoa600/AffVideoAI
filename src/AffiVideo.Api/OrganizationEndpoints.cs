using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AffiVideo.Api;

internal static class OrganizationEndpoints
{
    // An Organization that is not the caller's own is answered exactly like one
    // that does not exist: 404, from every endpoint here.
    public static void MapOrganizations(this IEndpointRouteBuilder routes)
    {
        var organization = routes.MapGroup("/organizations/{organizationId:guid}").WithTags("Organizations");

        organization.MapGet("", async Task<Results<Ok<OrganizationResponse>, NotFound>> (
                Guid organizationId, IOrganizations organizations, CancellationToken cancellationToken) =>
                await organizations.FindAsync(organizationId, cancellationToken) is { } found
                    ? TypedResults.Ok(found.ToResponse())
                    : TypedResults.NotFound())
            .WithName("GetOrganization")
            .WithSummary("The Organization's settings.");

        organization.MapPut("", async Task<Results<Ok<OrganizationResponse>, NotFound>> (
                Guid organizationId, UpdateOrganizationRequest request, IOrganizations organizations, CancellationToken cancellationToken) =>
                await organizations.RenameAsync(organizationId, request.Name, cancellationToken) is { } renamed
                    ? TypedResults.Ok(renamed.ToResponse())
                    : TypedResults.NotFound())
            .RequireAuthorization(SessionSetup.OwnerPolicy)
            .WithName("UpdateOrganization")
            .WithSummary("Changes the Organization's settings. Owners only.");

        organization.MapGet("/members", async Task<Results<Ok<PagedResponse<MemberResponse>>, NotFound>> (
                Guid organizationId, int? page, int? pageSize, IOrganizations organizations, CancellationToken cancellationToken) =>
                await organizations.ListMembersAsync(organizationId, new PageRequest(page, pageSize), cancellationToken) is { } members
                    ? TypedResults.Ok(members.ToResponse(ToResponse))
                    : TypedResults.NotFound())
            .WithName("ListMembers")
            .WithSummary("The Organization's members, by email.");

        organization.MapPost("/members", async Task<Results<Created<MemberResponse>, ValidationProblem, NotFound>> (
                Guid organizationId, AddMemberRequest request, IOrganizations organizations, CancellationToken cancellationToken) =>
            {
                var result = await organizations.AddEditorAsync(organizationId, request.Email, request.Password, cancellationToken);
                if (result is null) return TypedResults.NotFound();
                if (result.Member is null) return TypedResults.ValidationProblem(result.Errors);
                return TypedResults.Created($"/api/v1/organizations/{organizationId}/members", ToResponse(result.Member));
            })
            .RequireAuthorization(SessionSetup.OwnerPolicy)
            .WithName("AddMember")
            .WithSummary("Adds an Editor to the Organization. Owners only.");

        organization.MapGet("/audit-log", async Task<Results<Ok<PagedResponse<AuditLogEntryResponse>>, NotFound>> (
                Guid organizationId, int? page, int? pageSize, IOrganizations organizations, CancellationToken cancellationToken) =>
                await organizations.ReadAuditLogAsync(organizationId, new PageRequest(page, pageSize), cancellationToken) is { } entries
                    ? TypedResults.Ok(entries.ToResponse(e => new AuditLogEntryResponse(
                        e.Id, e.OrganizationId, e.ActorMemberId, e.ActorEmail, e.Action, e.SubjectId, e.OccurredAt)))
                    : TypedResults.NotFound())
            .RequireAuthorization(SessionSetup.OwnerPolicy)
            .WithName("ReadAuditLog")
            .WithSummary("The sensitive actions taken in the Organization, newest first. Owners only.");
    }

    public static OrganizationResponse ToResponse(this Organization organization) => new(organization.Id, organization.Name);

    private static MemberResponse ToResponse(MemberSummary member) => new(member.Id, member.Email, member.Role);
}
