using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Lab;

// No method names an Organization: the context's filter leaves only the caller's
// Campaigns and Variants. A Variant is only ever referenced: nothing here changes one.
internal sealed class ScopedCampaigns(AffiVideoDbContext database, Caller caller, TimeProvider clock) : ICampaigns
{
    public async Task<CampaignRecord> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var campaign = new Campaign(Guid.CreateVersion7(), CallingOrganization(), name, clock.GetUtcNow());
        database.Campaigns.Add(campaign);
        await database.SaveChangesAsync(cancellationToken);
        return new CampaignRecord(campaign, VariantCount: 0);
    }

    public async Task<CampaignRecord?> FindAsync(Guid campaignId, CancellationToken cancellationToken) =>
        await WithCount(database.Campaigns.AsNoTracking().Where(c => c.Id == campaignId))
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? new CampaignRecord(found.Campaign, found.VariantCount)
            : null;

    public async Task<Page<CampaignRecord>> ListAsync(CampaignStatus? status, PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.Campaigns.AsNoTracking();
        if (status is { } only)
        {
            all = all.Where(c => c.Status == only);
        }

        var items = await WithCount(all)
            .OrderByDescending(x => x.Campaign.CreatedAt).ThenByDescending(x => x.Campaign.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<CampaignRecord>(
            items.Select(x => new CampaignRecord(x.Campaign, x.VariantCount)).ToList(),
            page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public Task<CampaignRecord?> RenameAsync(Guid campaignId, string name, CancellationToken cancellationToken) =>
        ChangeAsync(campaignId, campaign => campaign.Rename(name, clock.GetUtcNow()), cancellationToken);

    public Task<CampaignRecord?> ArchiveAsync(Guid campaignId, CancellationToken cancellationToken) =>
        ChangeAsync(campaignId, campaign => campaign.Archive(clock.GetUtcNow()), cancellationToken);

    private async Task<CampaignRecord?> ChangeAsync(Guid campaignId, Action<Campaign> change, CancellationToken cancellationToken)
    {
        var campaign = await database.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null) return null;

        change(campaign);
        await database.SaveChangesAsync(cancellationToken);
        return await FindAsync(campaignId, cancellationToken);
    }

    public async Task<Page<CampaignVariantRecord>?> ListVariantsAsync(Guid campaignId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await CampaignExistsAsync(campaignId, cancellationToken)) return null;

        var all =
            from grouped in database.CampaignVariants.AsNoTracking()
            where grouped.CampaignId == campaignId
            join variant in database.Variants on grouped.VariantId equals variant.Id
            join project in database.Projects on variant.ProjectId equals project.Id
            join product in database.Products on project.ProductId equals product.Id
            select new { Variant = variant, ProductId = product.Id, ProductName = product.Name, grouped.AddedAt };
        var items = await all
            .OrderBy(x => x.AddedAt).ThenBy(x => x.Variant.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<CampaignVariantRecord>(
            items.Select(x => new CampaignVariantRecord(x.Variant, x.ProductId, x.ProductName, x.AddedAt)).ToList(),
            page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<bool> AddVariantAsync(Guid campaignId, Guid variantId, CancellationToken cancellationToken)
    {
        if (!await CampaignExistsAsync(campaignId, cancellationToken)) return false;
        if (!await database.Variants.AnyAsync(v => v.Id == variantId, cancellationToken)) return false;
        if (await database.CampaignVariants.AnyAsync(g => g.CampaignId == campaignId && g.VariantId == variantId, cancellationToken)) return true;

        database.CampaignVariants.Add(new CampaignVariant(campaignId, variantId, CallingOrganization(), clock.GetUtcNow()));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Added by someone else after the check above. It is in the Campaign, which is what was asked for.
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The Variant went with its Project after the check above.
            return false;
        }
    }

    public async Task<bool> RemoveVariantAsync(Guid campaignId, Guid variantId, CancellationToken cancellationToken) =>
        await database.CampaignVariants
            .Where(g => g.CampaignId == campaignId && g.VariantId == variantId)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private Task<bool> CampaignExistsAsync(Guid campaignId, CancellationToken cancellationToken) =>
        database.Campaigns.AnyAsync(c => c.Id == campaignId, cancellationToken);

    private Guid CallingOrganization() =>
        caller.OrganizationId ?? throw new InvalidOperationException("A Campaign belongs to the caller's Organization, and there is no caller.");

    private IQueryable<CampaignWithCount> WithCount(IQueryable<Campaign> campaigns) =>
        from campaign in campaigns
        select new CampaignWithCount
        {
            Campaign = campaign,
            VariantCount = database.CampaignVariants.Count(g => g.CampaignId == campaign.Id),
        };

    private sealed class CampaignWithCount
    {
        public required Campaign Campaign { get; init; }

        public required int VariantCount { get; init; }
    }
}
