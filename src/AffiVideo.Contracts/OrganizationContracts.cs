using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

public sealed record OrganizationResponse(Guid Id, string Name);

public sealed record UpdateOrganizationRequest([Required, StringLength(Organization.NameMaxLength)] string Name);

public sealed record MemberResponse(Guid Id, string Email, MemberRole Role);

/// <summary>The new member is an Editor and signs in with this email and password.</summary>
public sealed record AddMemberRequest([Required] string Email, [Required] string Password);

public sealed record AuditLogEntryResponse(
    Guid Id,
    Guid OrganizationId,
    Guid ActorMemberId,
    string ActorEmail,
    string Action,
    Guid? SubjectId,
    DateTimeOffset OccurredAt);
