using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AffiVideo.Infrastructure.Projects;

// No method names an Organization: the context's filter leaves only the caller's
// Products, Projects and Variants.
internal sealed class ScopedProjects(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    ILogger<ScopedProjects> logger) : IProjects
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

    public async Task<ProjectDeletion> DeleteAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await database.Projects.SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null) return ProjectDeletion.NotFound;
        var rendered =
            from video in database.RenderedVideos
            join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
            join variant in database.Variants on storyboard.VariantId equals variant.Id
            where variant.ProjectId == projectId
            select video.Id;
        if (await rendered.AnyAsync(cancellationToken)) return ProjectDeletion.HasRenderedVideos;
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can delete a Project.");

        // The database deletes the Project's Variants with it, and their Storyboards, render jobs and audio.
        // The audio's files are not the database's to delete: they are deleted here, once the records are gone.
        var audioFiles = await (
            from audio in database.VariantAudio.AsNoTracking()
            join variant in database.Variants on audio.VariantId equals variant.Id
            where variant.ProjectId == projectId
            select audio).ToListAsync(cancellationToken);
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
            return ProjectDeletion.NotFound;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // A render finished after the check above. The database kept the Project for its Rendered Video.
            return ProjectDeletion.HasRenderedVideos;
        }

        // A file left behind is wasted space, not a reason to fail the request.
        foreach (var key in audioFiles.Select(audio => audio.StorageKey))
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", key);
            }
        }
        return ProjectDeletion.Deleted;
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
