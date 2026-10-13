using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <param name="AffiliateLabEnabled">Whether the Organization has the Affiliate Lab. Without it, everything under <c>/api/v1/lab</c> answers 404.</param>
/// <param name="DefaultVoice">The voice an Owner chose for Projects whose brief names none. Null until one is chosen: it is then the first of the voices on offer.</param>
public sealed record OrganizationResponse(Guid Id, string Name, bool AffiliateLabEnabled, string? DefaultVoice);

/// <param name="DefaultVoice">The voice Projects are given when their brief names none: one of the voices on offer, by its id. Left out, it stays as it is.</param>
public sealed record UpdateOrganizationRequest(
    [Required, StringLength(Organization.NameMaxLength)] string Name,
    [StringLength(Project.VoiceMaxLength)] string? DefaultVoice = null);

/// <param name="Voices">The voices narration text can be spoken in.</param>
/// <param name="DefaultVoice">The id of the voice a Project of the caller's Organization is given when its brief names none.</param>
public sealed record VoicesResponse(IReadOnlyList<VoiceResponse> Voices, string DefaultVoice);

/// <param name="Id">What a Project's brief names it by.</param>
/// <param name="HasSample">Whether there is a recording of it to play before choosing.</param>
public sealed record VoiceResponse(string Id, string Name, bool HasSample);

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
