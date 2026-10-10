using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Lab;

public sealed class LabDashboardOptions
{
    public const string Section = "LabDashboard";
    public int MinimumPosts { get; set; } = 2;
    public long MinimumViews { get; set; } = 100;
}

internal sealed class ScopedLabDashboard(AffiVideoDbContext database, IOptions<LabDashboardOptions> configured) : ILabDashboard
{
    public async Task<LabDashboard> ReadAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        var posts = await (from post in database.PublishedPosts.AsNoTracking()
            join video in database.RenderedVideos on post.RenderedVideoId equals video.Id
            join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
            join variant in database.Variants on storyboard.VariantId equals variant.Id
            join project in database.Projects on variant.ProjectId equals project.Id
            join product in database.Products on project.ProductId equals product.Id
            select new PostRow(post.Id, post.AffiliateLinkId, product.Id, variant.Id,
                variant.CreativeTemplate, variant.Hook)).ToListAsync(cancellationToken);

        var postIds = posts.Select(p => p.Id).ToArray();
        var snapshots = await database.PerformanceSnapshots.AsNoTracking()
            .Where(s => postIds.Contains(s.PublishedPostId)).ToListAsync(cancellationToken);
        var latest = snapshots.GroupBy(s => s.PublishedPostId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(s => s.TakenAt).ThenByDescending(s => s.RecordedAt)
                .ThenByDescending(s => s.Id).First());
        var records = await database.CommissionRecords.AsNoTracking().ToListAsync(cancellationToken);
        var allProducts = await database.Products.AsNoTracking().Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);
        var campaignVariants = await database.CampaignVariants.AsNoTracking().ToListAsync(cancellationToken);
        var campaigns = await database.Campaigns.AsNoTracking().ToListAsync(cancellationToken);

        List<LabDashboardGroup> Group(IEnumerable<GroupSelection> groups) => groups
            .OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Identity.Key, StringComparer.Ordinal)
            .Select(g => Build(g.Identity, g.Name, posts.Where(g.Identity.Contains).ToList(), posts,
                latest, records, options)).ToList();

        var products = Group(allProducts.Select(product =>
            new GroupSelection(new ProductGroup(product.Id), product.Name)));
        var templates = Group(posts.Select(p => p.Template).Distinct().Select(template =>
            new GroupSelection(new TemplateGroup(template), template.ToString())));
        var hooks = Group(posts.Select(p => p.Hook).Distinct().Select(hook =>
            new GroupSelection(new HookGroup(hook), hook)));
        var campaignGroups = Group(campaigns.Select(c => new GroupSelection(
            new CampaignGroup(c.Id, campaignVariants.Where(v => v.CampaignId == c.Id)
                .Select(v => v.VariantId).ToHashSet()), c.Name)));
        return new LabDashboard(options.MinimumPosts, options.MinimumViews, products, templates, hooks, campaignGroups);
    }

    private static LabDashboardGroup Build(GroupIdentity identity, string name, IReadOnlyList<PostRow> members,
        IReadOnlyList<PostRow> allPosts, IReadOnlyDictionary<Guid, PerformanceSnapshot> latest,
        IReadOnlyList<CommissionRecord> records, LabDashboardOptions options)
    {
        LabMetric Metric(Func<PerformanceSnapshot, long?> read)
        {
            var values = members.Select(p => latest.GetValueOrDefault(p.Id)).ToList();
            var sources = members.Select(p => latest.GetValueOrDefault(p.Id) is { } snapshot
                    ? $"Performance Snapshot {snapshot.Id} ({snapshot.Source})"
                    : $"Published Post {p.Id} (no Performance Snapshot)")
                .Distinct().Order(StringComparer.Ordinal).ToList();
            return new LabMetric(values.Count > 0 && values.All(s => s is not null && read(s) is not null)
                ? values.Sum(s => read(s!)) : null, sources);
        }

        var views = Metric(s => s.Views);
        var likes = Metric(s => s.Likes);
        var comments = Metric(s => s.Comments);
        var shares = Metric(s => s.Shares);
        var clicks = Metric(s => s.Clicks);
        // A link can contribute only when every post carrying it is in this group.
        var links = members.Where(p => p.LinkId is not null).Select(p => p.LinkId!.Value).Distinct()
            .Where(link => allPosts.Where(p => p.LinkId == link).All(identity.Contains)).ToHashSet();
        var matched = records.Where(r => r.AffiliateLinkId is { } link && links.Contains(link)
            || identity is ProductGroup product && r.ProductId == product.ProductId).ToList();
        var commission = Commissions.Totals(matched);
        var clickRate = views.Value is > 0 && clicks.Value is not null
            ? decimal.Divide(clicks.Value.Value, views.Value.Value) : (decimal?)null;
        // Orders at Product level cannot be projected onto a creative group containing other Products.
        // Link records can be counted only if their posts belong wholly to this group.
        var productOrders = matched.Where(r => r.ProductId is not null).ToList();
        var linkOrders = matched.Where(r => r.AffiliateLinkId is not null).ToList();
        var ordersKnown = productOrders.Count > 0 && linkOrders.Count == 0 && productOrders.All(r => r.Orders is not null)
            || linkOrders.Count > 0 && productOrders.Count == 0 && linkOrders.All(r => r.Orders is not null)
                && members.All(p => p.LinkId is { } link && linkOrders.Any(r => r.AffiliateLinkId == link));
        var conversion = clicks.Value is > 0 && ordersKnown && matched.Select(r => r.Currency).Distinct().Count() <= 1
            ? decimal.Divide(matched.Sum(r => r.Orders!.Value), clicks.Value.Value) : (decimal?)null;
        return new LabDashboardGroup(identity.Key, name, members.Count,
            members.Count < Math.Max(1, options.MinimumPosts) || views.Value is null || views.Value < Math.Max(0, options.MinimumViews),
            views, likes, comments, shares, clicks, clickRate, conversion, commission,
            members.Select(p => $"Published Post {p.Id}").Order(StringComparer.Ordinal).ToList(),
            views.Sources.Concat(clicks.Sources).Distinct().Order(StringComparer.Ordinal).ToList(),
            clicks.Sources.Concat(matched.Select(r => $"Commission record {r.Id} ({r.Source})"))
                .Distinct().Order(StringComparer.Ordinal).ToList());
    }

    private sealed record PostRow(Guid Id, Guid? LinkId, Guid ProductId,
        Guid VariantId, CreativeTemplate Template, string Hook);

    private sealed record GroupSelection(GroupIdentity Identity, string Name);

    private abstract record GroupIdentity
    {
        public abstract string Key { get; }
        public abstract bool Contains(PostRow post);
    }

    private sealed record ProductGroup(Guid ProductId) : GroupIdentity
    {
        public override string Key => ProductId.ToString();
        public override bool Contains(PostRow post) => post.ProductId == ProductId;
    }

    private sealed record TemplateGroup(CreativeTemplate Template) : GroupIdentity
    {
        public override string Key => Template.ToString();
        public override bool Contains(PostRow post) => post.Template == Template;
    }

    private sealed record HookGroup(string Hook) : GroupIdentity
    {
        public override string Key => Hook;
        public override bool Contains(PostRow post) => post.Hook == Hook;
    }

    private sealed record CampaignGroup(Guid CampaignId, IReadOnlySet<Guid> VariantIds) : GroupIdentity
    {
        public override string Key => CampaignId.ToString();
        public override bool Contains(PostRow post) => VariantIds.Contains(post.VariantId);
    }
}
