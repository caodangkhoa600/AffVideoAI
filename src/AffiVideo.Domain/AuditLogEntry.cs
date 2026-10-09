namespace AffiVideo.Domain;

/// <summary>One sensitive action: who did what, in which Organization, and when. Never changed after it is written.</summary>
public sealed class AuditLogEntry(
    Guid id,
    Guid organizationId,
    Guid actorMemberId,
    string action,
    Guid? subjectId,
    DateTimeOffset occurredAt) : IOwnedByOrganization
{
    public Guid Id { get; private set; } = id;

    public Guid OrganizationId { get; private set; } = organizationId;

    /// <summary>The member who did it.</summary>
    public Guid ActorMemberId { get; private set; } = actorMemberId;

    /// <summary>One of <see cref="AuditActions"/>.</summary>
    public string Action { get; private set; } = action;

    /// <summary>The record the action was done to, when there is one.</summary>
    public Guid? SubjectId { get; private set; } = subjectId;

    public DateTimeOffset OccurredAt { get; private set; } = occurredAt;
}

public static class AuditActions
{
    public const string MemberAdded = "member.added";
}
