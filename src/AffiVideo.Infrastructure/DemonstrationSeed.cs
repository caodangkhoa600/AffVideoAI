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
        // Not created means its Owner was found, so the Owner is there to say which Organization it is.
        var organizationId = created
            ?? (await members.FindByEmailAsync(DemonstrationOrganization.OwnerEmail))?.OrganizationId
            ?? throw new InvalidOperationException("The demonstration Organization's Owner is missing.");

        // The system acts for the demonstration Organization from here on.
        caller.Identify(organizationId, memberId: null);
        if (await database.Products.AnyAsync(p => p.Id == DemonstrationProduct.Id, cancellationToken)) return created is not null;

        database.Products.Add(new Product(DemonstrationProduct.Id, organizationId, DemonstrationProduct.Details, clock.GetUtcNow()));
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }
}
