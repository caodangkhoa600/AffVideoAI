using AffiVideo.Domain;

namespace AffiVideo.Application.Organizations;

/// <summary>
/// An Organization, its members and its audit log, as the caller is allowed to
/// see them. Every method answers null for an Organization that is not the
/// caller's own, exactly as for one that does not exist.
/// </summary>
public interface IOrganizations
{
    Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<Organization?> RenameAsync(Guid organizationId, string name, CancellationToken cancellationToken);

    Task<Page<MemberSummary>?> ListMembersAsync(Guid organizationId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Adds an Editor who signs in with this email and password, and records it in the audit log.</summary>
    Task<AddMemberResult?> AddEditorAsync(Guid organizationId, string email, string password, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<Page<AuditLogRecord>?> ReadAuditLogAsync(Guid organizationId, PageRequest page, CancellationToken cancellationToken);
}

public sealed record MemberSummary(Guid Id, string Email, MemberRole Role);

/// <summary>Either the member that was added, or the reasons it was not, keyed by the field at fault ("email" or "password").</summary>
public sealed record AddMemberResult(MemberSummary? Member, IReadOnlyDictionary<string, string[]> Errors);

public sealed record AuditLogRecord(
    Guid Id,
    Guid OrganizationId,
    Guid ActorMemberId,
    string ActorEmail,
    string Action,
    Guid? SubjectId,
    DateTimeOffset OccurredAt);
