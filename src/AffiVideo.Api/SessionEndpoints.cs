using System.Security.Claims;
using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace AffiVideo.Api;

internal static class SessionEndpoints
{
    public static void MapSession(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/antiforgery-token", (HttpContext context, IAntiforgery antiforgery) =>
                TypedResults.Ok(new AntiforgeryTokenResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken")
            .WithSummary("The token every state-changing request must send in the X-CSRF-TOKEN header.")
            .WithTags("Session");

        routes.MapPost("/session", async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> (
                SignInRequest request,
                UserManager<Member> members,
                SignInManager<Member> signIn,
                Caller caller,
                IOrganizations organizations,
                CancellationToken cancellationToken) =>
            {
                var member = await members.FindByEmailAsync(request.Email.Trim());
                if (member is null)
                {
                    // Do the work of checking a password anyway, so the time taken
                    // does not tell an unknown email from a wrong password.
                    members.PasswordHasher.HashPassword(new Member(), request.Password);
                    return SignInRefused();
                }

                // The member's Organization is now known, and their row is about
                // to be written: the count of failed attempts lives on it.
                caller.Identify(member.OrganizationId, member.Id);
                var result = await signIn.PasswordSignInAsync(member, request.Password, isPersistent: false, lockoutOnFailure: true);
                if (!result.Succeeded) return SignInRefused();

                var organization = await organizations.FindAsync(member.OrganizationId, cancellationToken);
                return TypedResults.Ok(new SessionResponse(
                    new MemberResponse(member.Id, member.Email!, member.Role),
                    organization!.ToResponse()));
            })
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Signs a member in with email and password and starts their session.")
            .WithTags("Session");

        routes.MapGet("/session", async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult>> (
                ClaimsPrincipal user,
                Caller caller,
                IOrganizations organizations,
                CancellationToken cancellationToken) =>
            {
                var organization = await organizations.FindAsync(caller.OrganizationId!.Value, cancellationToken);
                if (organization is null) return TypedResults.Unauthorized();

                return TypedResults.Ok(new SessionResponse(
                    new MemberResponse(
                        user.MemberId(),
                        user.FindFirstValue(ClaimTypes.Email)!,
                        Enum.Parse<MemberRole>(user.FindFirstValue(ClaimTypes.Role)!)),
                    organization.ToResponse()));
            })
            .WithName("GetSession")
            .WithSummary("Who is signed in, and their Organization.")
            .WithTags("Session");

        routes.MapDelete("/session", async (SignInManager<Member> signIn) =>
            {
                await signIn.SignOutAsync();
                return TypedResults.NoContent();
            })
            .WithName("SignOut")
            .WithSummary("Ends the session.")
            .WithTags("Session");
    }

    // The same answer whether the email is unknown, the password is wrong or the
    // member is locked out after too many attempts.
    private static ProblemHttpResult SignInRefused() => TypedResults.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "The email or password is not correct.");
}
