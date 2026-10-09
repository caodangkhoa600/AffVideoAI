using AffiVideo.Application;
using AffiVideo.Application.Rendering;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Rendering;

// No method names an Organization: the context's filter leaves only the caller's
// Storyboards and jobs.
internal sealed class ScopedRenders(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IRenders
{
    public async Task<SubmittedRender?> SubmitAsync(
        Guid projectId, Guid variantId, int version, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (await FindStoryboardIdAsync(projectId, variantId, version, cancellationToken) is not { } storyboardId) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A render job is queued for the caller's Organization, and there is no caller.");
        if (await FindByKeyAsync(storyboardId, idempotencyKey, cancellationToken) is { } already) return new SubmittedRender(already, Queued: false);

        var job = new RenderJob(Guid.CreateVersion7(), organizationId, storyboardId, idempotencyKey, clock.GetUtcNow());
        database.RenderJobs.Add(job);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The Project, and the Storyboard with it, was deleted after it was read.
            return null;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same click, arriving twice at the same moment: the other request queued the job.
            database.ChangeTracker.Clear();
            return await FindByKeyAsync(storyboardId, idempotencyKey, cancellationToken) is { } theirs
                ? new SubmittedRender(theirs, Queued: false)
                : null;
        }
        return new SubmittedRender(job, Queued: true);
    }

    public async Task<Page<RenderJob>?> ListJobsAsync(
        Guid projectId, Guid variantId, int version, PageRequest page, CancellationToken cancellationToken)
    {
        if (await FindStoryboardIdAsync(projectId, variantId, version, cancellationToken) is not { } storyboardId) return null;

        var all = database.RenderJobs.AsNoTracking().Where(j => j.StoryboardId == storyboardId);
        var items = await all
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<RenderJob>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public Task<RenderJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        database.RenderJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

    public async Task<RenderJob?> CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        // A worker may move the job on between its being read and saved here; then it is read again.
        for (var attempt = 1; ; attempt++)
        {
            var job = await database.RenderJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);
            if (job is null || !job.Cancel(clock.GetUtcNow())) return job;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return job;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                database.ChangeTracker.Clear();
            }
        }
    }

    private Task<RenderJob?> FindByKeyAsync(Guid storyboardId, string idempotencyKey, CancellationToken cancellationToken) =>
        database.RenderJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.StoryboardId == storyboardId && j.IdempotencyKey == idempotencyKey, cancellationToken);

    // The version has to be this Variant's, and the Variant this Project's: an
    // identifier from one does not work under another.
    private async Task<Guid?> FindStoryboardIdAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken)
    {
        if (!await database.Variants.AnyAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken)) return null;
        return await database.Storyboards
            .Where(s => s.VariantId == variantId && s.Version == version)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
