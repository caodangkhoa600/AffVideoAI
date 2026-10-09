using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Projects;

// No method names an Organization: the context's filter leaves only the caller's
// Projects and Variants. A Variant is only ever added: nothing here changes one.
internal sealed class ScopedVariants(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IVariants
{
    public async Task<Variant?> AddAsync(Guid projectId, CreativeTemplate creativeTemplate, string hook, CancellationToken cancellationToken)
    {
        if (!await ProjectExistsAsync(projectId, cancellationToken)) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Variant is added for the caller's Organization, and there is no caller.");

        var variant = new Variant(Guid.CreateVersion7(), organizationId, projectId, creativeTemplate, hook, clock.GetUtcNow());
        return await SaveAsync(variant, cancellationToken) ? variant : null;
    }

    public async Task<Page<Variant>?> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await ProjectExistsAsync(projectId, cancellationToken)) return null;

        var all = database.Variants.AsNoTracking().Where(v => v.ProjectId == projectId);
        var items = await all
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<Variant>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    // The Variant has to be this Project's: an identifier from one Project does not work under another.
    public Task<Variant?> FindAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        database.Variants.AsNoTracking().SingleOrDefaultAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken);

    public async Task<VariantDuplication?> DuplicateAsync(Guid projectId, Guid variantId, string hook, CancellationToken cancellationToken)
    {
        var original = await FindAsync(projectId, variantId, cancellationToken);
        if (original is null) return null;
        if (original.HasHook(hook))
        {
            return VariantDuplication.Refuse("Enter a Hook that differs from the one this Variant has. A different Hook is what makes a different Variant.");
        }

        var duplicate = original.DuplicateWith(Guid.CreateVersion7(), hook, clock.GetUtcNow());
        return await SaveAsync(duplicate, cancellationToken) ? VariantDuplication.Made(duplicate) : null;
    }

    /// <returns>False, with nothing saved, when the Project was deleted after it was read.</returns>
    private async Task<bool> SaveAsync(Variant variant, CancellationToken cancellationToken)
    {
        database.Variants.Add(variant);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return false;
        }
    }

    private Task<bool> ProjectExistsAsync(Guid projectId, CancellationToken cancellationToken) =>
        database.Projects.AnyAsync(p => p.Id == projectId, cancellationToken);
}
