using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Organizations;

// No method compares the Organization asked for with the caller's own: the
// context's filters make another Organization, and everything in it, absent.
internal sealed class ScopedOrganizations(
    AffiVideoDbContext database,
    UserManager<Member> members,
    Caller caller,
    TimeProvider clock) : IOrganizations
{
    public Task<Organization?> FindAsync(Guid organizationId, CancellationToken cancellationToken) =>
        database.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

    public async Task<Organization?> RenameAsync(Guid organizationId, string name, CancellationToken cancellationToken)
    {
        var organization = await FindAsync(organizationId, cancellationToken);
        if (organization is null) return null;

        organization.Rename(name.Trim());
        await database.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<Page<MemberSummary>?> ListMembersAsync(Guid organizationId, PageRequest page, CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationId, cancellationToken) is null) return null;

        var all = database.Members.Where(m => m.OrganizationId == organizationId);
        var items = await all
            .OrderBy(m => m.Email).ThenBy(m => m.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(m => new MemberSummary(m.Id, m.Email!, m.Role))
            .ToListAsync(cancellationToken);
        return new Page<MemberSummary>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<AddMemberResult?> AddEditorAsync(Guid organizationId, string email, string password, CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationId, cancellationToken) is null) return null;
        var actor = caller.MemberId ?? throw new InvalidOperationException("Only a member can add a member.");

        email = email.Trim();
        var member = new Member
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Role = MemberRole.Editor,
            UserName = email,
            Email = email,
        };

        // The member and the record of who added them are written together or not at all.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var created = await members.CreateAsync(member, password);
        if (!created.Succeeded) return new AddMemberResult(null, ByField(created.Errors));

        database.AuditLog.Add(new AuditLogEntry(
            Guid.CreateVersion7(), organizationId, actor, AuditActions.MemberAdded, member.Id, clock.GetUtcNow()));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AddMemberResult(new MemberSummary(member.Id, email, member.Role), new Dictionary<string, string[]>());
    }

    public async Task<Page<AuditLogRecord>?> ReadAuditLogAsync(Guid organizationId, PageRequest page, CancellationToken cancellationToken)
    {
        if (await FindAsync(organizationId, cancellationToken) is null) return null;

        var all = database.AuditLog.Where(e => e.OrganizationId == organizationId);
        var items = await all
            .Join(database.Members, e => e.ActorMemberId, m => m.Id, (e, m) => new { Entry = e, ActorEmail = m.Email! })
            .OrderByDescending(x => x.Entry.OccurredAt).ThenByDescending(x => x.Entry.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(x => new AuditLogRecord(
                x.Entry.Id, x.Entry.OrganizationId, x.Entry.ActorMemberId, x.ActorEmail, x.Entry.Action, x.Entry.SubjectId, x.Entry.OccurredAt))
            .ToListAsync(cancellationToken);
        return new Page<AuditLogRecord>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    // A member's email is also their user name, so the identity system reports a
    // problem with it twice; only the email wording is kept.
    private static Dictionary<string, string[]> ByField(IEnumerable<IdentityError> errors) => errors
        .Where(e => !e.Code.Contains("UserName", StringComparison.Ordinal))
        .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
        .ToDictionary(group => group.Key, group => group.Select(e => e.Description).ToArray());
}
