using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Projects;

// No method names an Organization: the context's filter leaves only the caller's
// Products, Projects and Variants.
internal sealed class ScopedProjects(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IProjects
{
    public async Task<ProjectRecord?> CreateAsync(Guid productId, ProjectBrief brief, CancellationToken cancellationToken)
    {
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Project is created for the caller's Organization, and there is no caller.");
        var product = await database.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null) return null;

        var project = new Project(Guid.CreateVersion7(), organizationId, productId, brief, clock.GetUtcNow());
        database.Projects.Add(project);
        await database.SaveChangesAsync(cancellationToken);
        return new ProjectRecord(project, product.Name, VariantCount: 0);
    }

    public async Task<ProjectRecord?> FindAsync(Guid projectId, CancellationToken cancellationToken) =>
        await WithProduct(database.Projects.AsNoTracking().Where(p => p.Id == projectId))
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? ToRecord(found)
            : null;

    public async Task<Page<ProjectRecord>> ListAsync(Guid? productId, PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.Projects.AsNoTracking();
        if (productId is { } only)
        {
            all = all.Where(p => p.ProductId == only);
        }

        var items = await WithProduct(all)
            .OrderByDescending(x => x.Project.CreatedAt).ThenByDescending(x => x.Project.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<ProjectRecord>(
            items.Select(ToRecord).ToList(), page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await database.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null) return false;
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can delete a Project.");

        // The database deletes the Project's Variants with it.
        database.Projects.Remove(project);
        database.AuditLog.Add(new AuditLogEntry(
            Guid.CreateVersion7(), project.OrganizationId, member, AuditActions.ProjectDeleted, project.Id, clock.GetUtcNow()));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else deleted it after it was read. Nothing of this attempt is saved.
            return false;
        }
        return true;
    }

    private IQueryable<ProjectWithProduct> WithProduct(IQueryable<Project> projects) =>
        from project in projects
        join product in database.Products on project.ProductId equals product.Id
        select new ProjectWithProduct
        {
            Project = project,
            ProductName = product.Name,
            VariantCount = database.Variants.Count(v => v.ProjectId == project.Id),
        };

    private static ProjectRecord ToRecord(ProjectWithProduct found) => new(found.Project, found.ProductName, found.VariantCount);

    private sealed class ProjectWithProduct
    {
        public required Project Project { get; init; }

        public required string ProductName { get; init; }

        public required int VariantCount { get; init; }
    }
}
