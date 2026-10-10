using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AffiVideo.Infrastructure.Lab;

// No method names an Organization: the context's filter leaves only the caller's accounts.
internal sealed class ScopedSocialAccounts(AffiVideoDbContext database, Caller caller, TimeProvider clock) : ISocialAccounts
{
    public async Task<SocialAccount?> CreateAsync(SocialPlatform platform, string handle, CancellationToken cancellationToken)
    {
        var account = new SocialAccount(Guid.CreateVersion7(), caller.Organization("A social account"), platform, handle, clock.GetUtcNow());
        if (await database.SocialAccounts.AnyAsync(a => a.Platform == platform && a.Handle == account.Handle, cancellationToken)) return null;

        database.SocialAccounts.Add(account);
        // Null when someone else recorded it after the check above.
        return await database.SaveUnlessRecordedMeanwhileAsync(cancellationToken) ? account : null;
    }

    public async Task<Page<SocialAccount>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.SocialAccounts.AsNoTracking();
        var items = await all
            .OrderBy(a => a.Platform).ThenBy(a => a.Handle).ThenBy(a => a.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<SocialAccount>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }
}

// No method names an Organization: the context's filter leaves only the caller's links.
internal sealed class ScopedAffiliateLinks(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IAffiliateLinks
{
    public async Task<AffiliateLinkRecord?> CreateAsync(string url, string? label, CancellationToken cancellationToken)
    {
        var link = new AffiliateLink(Guid.CreateVersion7(), caller.Organization("An affiliate link"), url, label, clock.GetUtcNow());
        if (await database.AffiliateLinks.AnyAsync(l => l.Url == link.Url, cancellationToken)) return null;

        database.AffiliateLinks.Add(link);
        // Null when someone else recorded it after the check above.
        return await database.SaveUnlessRecordedMeanwhileAsync(cancellationToken) ? new AffiliateLinkRecord(link, PublishedPostCount: 0) : null;
    }

    public async Task<Page<AffiliateLinkRecord>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.AffiliateLinks.AsNoTracking();
        var items = await all
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .Select(l => new { Link = l, PublishedPostCount = database.PublishedPosts.Count(p => p.AffiliateLinkId == l.Id) })
            .ToListAsync(cancellationToken);
        return new Page<AffiliateLinkRecord>(
            items.Select(x => new AffiliateLinkRecord(x.Link, x.PublishedPostCount)).ToList(),
            page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }
}

// No method names an Organization: the context's filter leaves only the caller's
// Published Posts, and only the caller's Rendered Videos, accounts and links to
// record one from. Nothing here changes any of those, and nothing is posted.
internal sealed class ScopedPublishedPosts(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IPublishedPosts
{
    private const string UrlAlreadyRecorded = "This URL is already recorded as a Published Post.";
    private const string NoSuchRenderedVideo = "There is no such Rendered Video in your Organization.";

    public async Task<PublishedPostOutcome> RecordAsync(NewPublishedPost post, CancellationToken cancellationToken)
    {
        var video = await database.RenderedVideos.AsNoTracking().SingleOrDefaultAsync(v => v.Id == post.RenderedVideoId, cancellationToken);
        var unknown = new Dictionary<string, string>();
        if (video is null) unknown["renderedVideoId"] = NoSuchRenderedVideo;
        if (!await database.SocialAccounts.AnyAsync(a => a.Id == post.SocialAccountId, cancellationToken))
        {
            unknown["socialAccountId"] = "There is no such social account in your Organization.";
        }
        if (post.AffiliateLinkId is { } linkId && !await database.AffiliateLinks.AnyAsync(l => l.Id == linkId, cancellationToken))
        {
            unknown["affiliateLinkId"] = "There is no such affiliate link in your Organization.";
        }
        if (video is null || unknown.Count > 0) return PublishedPostOutcome.NoSuch(unknown);

        if (video.State != RenderedVideoState.Approved)
        {
            return PublishedPostOutcome.Refuse(
                "This Rendered Video has not been approved. Only an approved Rendered Video can be recorded as a Published Post.");
        }

        var recorded = new PublishedPost(
            Guid.CreateVersion7(), caller.Organization("A Published Post"), video.Id, post.SocialAccountId, post.AffiliateLinkId,
            post.PublishedOn, post.Url, clock.GetUtcNow());
        if (await database.PublishedPosts.AnyAsync(p => p.Url == recorded.Url, cancellationToken))
        {
            return PublishedPostOutcome.Refuse(UrlAlreadyRecorded);
        }

        database.PublishedPosts.Add(recorded);
        try
        {
            if (!await database.SaveUnlessRecordedMeanwhileAsync(cancellationToken)) return PublishedPostOutcome.Refuse(UrlAlreadyRecorded);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Accounts and links are never deleted: it is the Rendered Video that went after the check above.
            return PublishedPostOutcome.NoSuch(new Dictionary<string, string> { ["renderedVideoId"] = NoSuchRenderedVideo });
        }

        return PublishedPostOutcome.Recorded((await FindAsync(recorded.Id, cancellationToken))!);
    }

    public async Task<PublishedPostRecord?> FindAsync(Guid postId, CancellationToken cancellationToken) =>
        await InContext(database.PublishedPosts.AsNoTracking().Where(p => p.Id == postId))
            .SingleOrDefaultAsync(cancellationToken) is { } found
            ? ToRecord(found)
            : null;

    public async Task<Page<PublishedPostRecord>> ListAsync(PublishedPostFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var all = InContext(database.PublishedPosts.AsNoTracking());
        if (filter.CampaignId is { } campaignId)
        {
            all = all.Where(x => database.CampaignVariants.Any(g => g.CampaignId == campaignId && g.VariantId == x.VariantId));
        }
        if (filter.ProductId is { } productId)
        {
            all = all.Where(x => x.ProductId == productId);
        }
        if (filter.VariantId is { } variantId)
        {
            all = all.Where(x => x.VariantId == variantId);
        }
        if (filter.Platform is { } platform)
        {
            all = all.Where(x => x.Account.Platform == platform);
        }
        if (filter.SocialAccountId is { } accountId)
        {
            all = all.Where(x => x.Post.SocialAccountId == accountId);
        }

        var items = await all
            .OrderByDescending(x => x.Post.PublishedOn).ThenByDescending(x => x.Post.CreatedAt).ThenByDescending(x => x.Post.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<PublishedPostRecord>(
            items.Select(ToRecord).ToList(), page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    // A Published Post holds its Rendered Video, which is kept with its Storyboard, and so
    // with the Variant, Project and Product it was made from: every one of them is there to be joined.
    private IQueryable<PostInContext> InContext(IQueryable<PublishedPost> posts) =>
        from post in posts
        join account in database.SocialAccounts on post.SocialAccountId equals account.Id
        join video in database.RenderedVideos on post.RenderedVideoId equals video.Id
        join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
        join variant in database.Variants on storyboard.VariantId equals variant.Id
        join project in database.Projects on variant.ProjectId equals project.Id
        join product in database.Products on project.ProductId equals product.Id
        join link in database.AffiliateLinks on post.AffiliateLinkId equals (Guid?)link.Id into links
        from carried in links.DefaultIfEmpty()
        select new PostInContext
        {
            Post = post,
            Account = account,
            AffiliateLink = carried,
            AffiliateLinkShared = post.AffiliateLinkId != null
                && database.PublishedPosts.Any(other => other.AffiliateLinkId == post.AffiliateLinkId && other.Id != post.Id),
            ProductId = product.Id,
            ProductName = product.Name,
            ProjectId = project.Id,
            VariantId = variant.Id,
            CreativeTemplate = variant.CreativeTemplate,
            Hook = variant.Hook,
        };

    private static PublishedPostRecord ToRecord(PostInContext found) => new(
        found.Post, found.Account, found.AffiliateLink, found.AffiliateLinkShared,
        found.ProductId, found.ProductName, found.ProjectId, found.VariantId, found.CreativeTemplate, found.Hook);

    private sealed class PostInContext
    {
        public required PublishedPost Post { get; init; }

        public required SocialAccount Account { get; init; }

        public required AffiliateLink? AffiliateLink { get; init; }

        public required bool AffiliateLinkShared { get; init; }

        public required Guid ProductId { get; init; }

        public required string ProductName { get; init; }

        public required Guid ProjectId { get; init; }

        public required Guid VariantId { get; init; }

        public required CreativeTemplate CreativeTemplate { get; init; }

        public required string Hook { get; init; }
    }
}

internal static class LabRecords
{
    /// <summary>Whose the new record is. Only a caller acting for an Organization can make one.</summary>
    public static Guid Organization(this Caller caller, string what) =>
        caller.OrganizationId ?? throw new InvalidOperationException($"{what} belongs to the caller's Organization, and there is no caller.");

    /// <summary>Saves what was added, unless the same thing was recorded by someone else in the meantime.</summary>
    /// <returns>False, with nothing saved, when it was.</returns>
    public static async Task<bool> SaveUnlessRecordedMeanwhileAsync(this AffiVideoDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }
}
