using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Identity;

/// <summary>Puts the member's Organization and role into the session, so a request can be scoped and authorised without a lookup.</summary>
internal sealed class MemberClaimsPrincipalFactory(UserManager<Member> members, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<Member>(members, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Member user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(MemberClaims.Organization, user.OrganizationId.ToString()));
        identity.AddClaim(new Claim(identity.RoleClaimType, user.Role.ToString()));
        return identity;
    }
}

public static class MemberClaims
{
    /// <summary>The identifier of the Organization the member belongs to.</summary>
    public const string Organization = "affivideo:organization";
}
