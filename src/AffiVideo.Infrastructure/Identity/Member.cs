using AffiVideo.Domain;
using Microsoft.AspNetCore.Identity;

namespace AffiVideo.Infrastructure.Identity;

/// <summary>
/// A person who signs in, as the identity system stores them. A member belongs to
/// exactly one Organization and signs in with their email, which is unique
/// across all Organizations.
/// </summary>
public sealed class Member : IdentityUser<Guid>, IOwnedByOrganization
{
    public Guid OrganizationId { get; init; }

    public MemberRole Role { get; init; }
}
