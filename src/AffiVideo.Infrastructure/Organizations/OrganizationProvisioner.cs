using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Organizations;

internal sealed class OrganizationProvisioner(
    AffiVideoDbContext database,
    UserManager<Member> members,
    Caller caller) : IOrganizationProvisioner
{
    public async Task<Guid?> CreateAsync(string name, string ownerEmail, string ownerPassword, CancellationToken cancellationToken)
    {
        if (await members.FindByEmailAsync(ownerEmail) is not null) return null;

        var organization = new Organization(Guid.CreateVersion7(), name);
        // From here on the system acts for the Organization it is creating.
        caller.Identify(organization.Id, memberId: null);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        database.Organizations.Add(organization);
        var owner = new Member
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organization.Id,
            Role = MemberRole.Owner,
            UserName = ownerEmail,
            Email = ownerEmail,
        };
        var created = await members.CreateAsync(owner, ownerPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"The Owner of '{name}' could not be created: {string.Join(" ", created.Errors.Select(e => e.Description))}");
        }
        await transaction.CommitAsync(cancellationToken);
        return organization.Id;
    }

    public async Task<bool> EnableAffiliateLabAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        // The system acts for the Organization it is changing.
        caller.Identify(organizationId, memberId: null);
        var organization = await database.Organizations.SingleAsync(o => o.Id == organizationId, cancellationToken);
        if (organization.AffiliateLabEnabled) return false;

        organization.EnableAffiliateLab();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
