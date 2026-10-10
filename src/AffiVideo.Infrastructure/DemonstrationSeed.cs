using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Application.Products;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure;

/// <summary>
/// What the seed command runs. Each thing is added only if it is missing, so a
/// database seeded before a later ticket gains what that ticket added, and
/// nothing a member has changed since is put back.
/// </summary>
internal sealed class DemonstrationSeed(
    IOrganizationProvisioner provisioner,
    UserManager<Member> members,
    AffiVideoDbContext database,
    Caller caller,
    TimeProvider clock)
{
    /// <returns>Whether anything was created.</returns>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var created = await provisioner.CreateAsync(
            DemonstrationOrganization.Name,
            DemonstrationOrganization.OwnerEmail,
            DemonstrationOrganization.OwnerPassword,
            cancellationToken);
        // Created or found, its Owner is there to say which Organization it is.
        var owner = await members.FindByEmailAsync(DemonstrationOrganization.OwnerEmail)
            ?? throw new InvalidOperationException("The demonstration Organization's Owner is missing.");
        var anythingCreated = created is not null;

        // The system acts for the demonstration Organization from here on.
        caller.Identify(owner.OrganizationId, memberId: null);
        // Our own affiliate operation runs in this Organization (ADR 0001).
        anythingCreated |= await provisioner.EnableAffiliateLabAsync(owner.OrganizationId, cancellationToken);
        var now = clock.GetUtcNow();
        if (!await database.Products.AnyAsync(p => p.Id == DemonstrationProduct.Id, cancellationToken))
        {
            database.Products.Add(new Product(DemonstrationProduct.Id, owner.OrganizationId, DemonstrationProduct.Details, now));
            anythingCreated = true;
        }

        // A Fact a member has withdrawn is still there, and so is not added again.
        var ids = DemonstrationFacts.All.Select(fact => fact.Id).ToArray();
        var present = await database.Facts.Where(f => ids.Contains(f.Id)).Select(f => f.Id).ToListAsync(cancellationToken);
        foreach (var (id, details) in DemonstrationFacts.All.Where(fact => !present.Contains(fact.Id)))
        {
            // Confirmed in the Owner's name and recorded as such, as if the Owner had done it.
            var fact = new Fact(id, owner.OrganizationId, DemonstrationProduct.Id, details, now);
            fact.Confirm(owner.Id, now);
            database.Facts.Add(fact);
            database.AuditLog.Add(new AuditLogEntry(
                Guid.CreateVersion7(), owner.OrganizationId, owner.Id, AuditActions.FactConfirmed, id, now));
            anythingCreated = true;
        }

        await database.SaveChangesAsync(cancellationToken);
        return anythingCreated;
    }
}
