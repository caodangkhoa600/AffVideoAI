using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Lab;

// No method names an Organization: the context's filter leaves only the caller's
// records, and only the caller's affiliate links and Products to attach one to.
internal sealed class ScopedCommissionRecords(AffiVideoDbContext database, Caller caller, TimeProvider clock) : ICommissionRecords
{
    private const string AlreadyRecorded =
        "A Commission record from this report, in this currency, for this period is already there. " +
        "Delete it first if its figures are wrong.";

    public async Task<CommissionRecordOutcome> RecordManualAsync(NewCommissionRecord record, CancellationToken cancellationToken)
    {
        if (record.Target.AffiliateLinkId is { } linkId && !await database.AffiliateLinks.AnyAsync(l => l.Id == linkId, cancellationToken))
        {
            return CommissionRecordOutcome.Missing("affiliateLinkId", "There is no such affiliate link in your Organization.");
        }
        if (record.Target.ProductId is { } productId && !await database.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return CommissionRecordOutcome.Missing("productId", "There is no such Product in your Organization.");
        }

        var recorded = new CommissionRecord(
            Guid.CreateVersion7(), caller.Organization("A Commission record"), record.Target, record.PeriodStart, record.PeriodEnd,
            record.Report, record.Currency, record.Figures, CommissionSource.Manual, clock.GetUtcNow());
        // The same report typed in twice would count its Commission twice.
        if (await database.CommissionRecords.AnyAsync(
                c => c.AffiliateLinkId == recorded.AffiliateLinkId && c.ProductId == recorded.ProductId
                    && c.Report == recorded.Report && c.Currency == recorded.Currency
                    && c.PeriodStart == recorded.PeriodStart && c.PeriodEnd == recorded.PeriodEnd,
                cancellationToken))
        {
            return CommissionRecordOutcome.Refuse(AlreadyRecorded);
        }

        database.CommissionRecords.Add(recorded);
        if (!await database.SaveUnlessRecordedMeanwhileAsync(cancellationToken)) return CommissionRecordOutcome.Refuse(AlreadyRecorded);

        // As it is kept: the moment it is answered with is the one it is read back with.
        return CommissionRecordOutcome.Recorded(
            await WithOverlap(database.CommissionRecords.AsNoTracking().Where(c => c.Id == recorded.Id)).SingleAsync(cancellationToken));
    }

    public async Task<Page<CommissionRecordEntry>> ListAsync(
        CommissionRecordFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.CommissionRecords.AsNoTracking();
        if (filter.AffiliateLinkId is { } linkId)
        {
            all = all.Where(c => c.AffiliateLinkId == linkId);
        }
        if (filter.ProductId is { } productId)
        {
            all = all.Where(c => c.ProductId == productId);
        }

        var items = await WithOverlap(all
                .OrderByDescending(c => c.PeriodEnd).ThenByDescending(c => c.PeriodStart)
                .ThenByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id)
                .Skip(page.Skip).Take(page.PageSize))
            .ToListAsync(cancellationToken);
        return new Page<CommissionRecordEntry>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can delete a Commission record.");
        var record = await database.CommissionRecords.SingleOrDefaultAsync(c => c.Id == recordId, cancellationToken);
        if (record is null) return false;

        database.CommissionRecords.Remove(record);
        database.AuditLog.Add(new AuditLogEntry(
            Guid.CreateVersion7(), record.OrganizationId, member, AuditActions.CommissionRecordDeleted, record.Id, clock.GetUtcNow()));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else deleted it in the meantime, and wrote the entry: to this caller there was no such record.
            return false;
        }
        return true;
    }

    public async Task<LinkCommission?> ForLinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var link = await database.AffiliateLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null) return null;

        var carriedBy = await database.LinksCarried().Where(x => x.AffiliateLinkId == linkId).ToListAsync(cancellationToken);
        var records = await database.CommissionRecords.AsNoTracking().Where(c => c.AffiliateLinkId == linkId).ToListAsync(cancellationToken);
        return new LinkCommission(
            link, carriedBy.Count, [.. carriedBy.Select(x => x.ProductId).Distinct().Order()], Commissions.Totals(records));
    }

    public async Task<ProductCommission?> ForProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await database.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null) return null;

        var carried = database.LinksCarried();
        // A link that a Published Post of another Product carries too says nothing about this Product alone.
        var records = await database.CommissionRecords.AsNoTracking()
            .Where(c => c.ProductId == productId
                || (carried.Any(x => x.AffiliateLinkId == c.AffiliateLinkId && x.ProductId == productId)
                    && !carried.Any(x => x.AffiliateLinkId == c.AffiliateLinkId && x.ProductId != productId)))
            .ToListAsync(cancellationToken);
        // Kept apart: a report for the Product may already hold what a report for one of its links holds.
        return new ProductCommission(
            product.Id, product.Name,
            Commissions.Totals(records.Where(c => c.ProductId is not null)),
            Commissions.Totals(records.Where(c => c.AffiliateLinkId is not null)));
    }

    // CommissionRecord.Overlaps, written out: a method of ours cannot be translated in here.
    private IQueryable<CommissionRecordEntry> WithOverlap(IQueryable<CommissionRecord> records) =>
        records.Select(c => new CommissionRecordEntry(
            c,
            database.CommissionRecords.Any(o =>
                o.Id != c.Id && o.AffiliateLinkId == c.AffiliateLinkId && o.ProductId == c.ProductId && o.Currency == c.Currency
                && o.PeriodStart <= c.PeriodEnd && o.PeriodEnd >= c.PeriodStart)));
}

internal static class CommissionQueries
{
    /// <summary>
    /// Each Published Post that carries an affiliate link, with the link and the Product the post is of.
    /// A Published Post holds its Rendered Video, which is kept with its Storyboard, and so with the
    /// Product it was made from.
    /// </summary>
    public static IQueryable<LinkCarried> LinksCarried(this AffiVideoDbContext database) =>
        from post in database.PublishedPosts
        where post.AffiliateLinkId != null
        join video in database.RenderedVideos on post.RenderedVideoId equals video.Id
        join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
        join variant in database.Variants on storyboard.VariantId equals variant.Id
        join project in database.Projects on variant.ProjectId equals project.Id
        select new LinkCarried { AffiliateLinkId = post.AffiliateLinkId, ProductId = project.ProductId };

    /// <summary>The records of each of the affiliate links, by link. A link with no record is left out.</summary>
    public static async Task<ILookup<Guid, CommissionRecord>> CommissionRecordsByLinkAsync(
        this AffiVideoDbContext database, IReadOnlyCollection<Guid> linkIds, CancellationToken cancellationToken)
    {
        if (linkIds.Count == 0) return Enumerable.Empty<CommissionRecord>().ToLookup(c => Guid.Empty);

        var records = await database.CommissionRecords.AsNoTracking()
            .Where(c => c.AffiliateLinkId != null && linkIds.Contains(c.AffiliateLinkId.Value))
            .ToListAsync(cancellationToken);
        return records.ToLookup(c => c.AffiliateLinkId!.Value);
    }

    internal sealed class LinkCarried
    {
        public required Guid? AffiliateLinkId { get; init; }

        public required Guid ProductId { get; init; }
    }
}
