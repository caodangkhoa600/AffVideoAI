using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Lab;

/// <summary>Below either, a group is too small to compare with another.</summary>
public sealed class LabDashboardOptions
{
    public const string Section = "LabDashboard";

    public int MinimumPublishedPosts { get; set; } = 2;

    public long MinimumViews { get; set; } = 100;
}

// No method names an Organization: the context's filter leaves only the caller's records.
// Everything the Organization has published is read at once and grouped here, which suits
// the tens of Published Posts of an experiment, not thousands.
internal sealed class ScopedLabDashboard(AffiVideoDbContext database, IOptions<LabDashboardOptions> configured) : ILabDashboard
{
    public async Task<LabDashboard> ReadAsync(CancellationToken cancellationToken)
    {
        var options = configured.Value;
        // A Published Post holds its Rendered Video, which is kept with its Storyboard, and so with the Variant and Product it was made from.
        var posts = await (
            from post in database.PublishedPosts.AsNoTracking()
            join video in database.RenderedVideos on post.RenderedVideoId equals video.Id
            join storyboard in database.Storyboards on video.StoryboardId equals storyboard.Id
            join variant in database.Variants on storyboard.VariantId equals variant.Id
            join project in database.Projects on variant.ProjectId equals project.Id
            select new Post(
                post.Id, post.AffiliateLinkId, post.PublishedOn, project.ProductId, variant.Id, variant.CreativeTemplate, variant.Hook,
                // The order of PerformanceSnapshotQueries.LatestFirst, written out: a method of ours cannot be translated in here.
                database.PerformanceSnapshots
                    .Where(s => s.PublishedPostId == post.Id)
                    .OrderByDescending(s => s.TakenAt).ThenByDescending(s => s.RecordedAt).ThenByDescending(s => s.Id)
                    .FirstOrDefault())).ToListAsync(cancellationToken);
        var records = await database.CommissionRecords.AsNoTracking().ToListAsync(cancellationToken);
        var products = await database.Products.AsNoTracking().Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        var campaigns = await database.Campaigns.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(cancellationToken);
        var grouped = (await database.CampaignVariants.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(g => g.CampaignId, g => g.VariantId);

        var everything = new Recorded(posts, records, options);
        List<LabDashboardGroup> Rows(IEnumerable<Group> groups) =>
            [.. groups.OrderBy(g => g.Name, StringComparer.Ordinal).ThenBy(g => g.Key, StringComparer.Ordinal).Select(everything.Row)];

        return new LabDashboard(
            options.MinimumPublishedPosts,
            options.MinimumViews,
            Rows(products
                .Where(product => posts.Any(p => p.ProductId == product.Id) || records.Any(r => r.ProductId == product.Id))
                .Select(product => new Group(product.Id.ToString(), product.Name, p => p.ProductId == product.Id, product.Id))),
            Rows(posts.Select(p => p.CreativeTemplate).Distinct()
                .Select(template => new Group(template.ToString(), template.ToString(), p => p.CreativeTemplate == template))),
            Rows(posts.Select(p => p.Hook).Distinct().Select(hook => new Group(hook, hook, p => p.Hook == hook))),
            Rows(campaigns.Select(campaign =>
            {
                var variants = grouped[campaign.Id].ToHashSet();
                return new Group(campaign.Id.ToString(), campaign.Name, p => variants.Contains(p.VariantId));
            })));
    }

    private sealed record Post(
        Guid Id, Guid? LinkId, DateOnly PublishedOn, Guid ProductId, Guid VariantId, CreativeTemplate CreativeTemplate, string Hook,
        PerformanceSnapshot? Latest);

    /// <param name="Holds">Whether a Published Post is in the group.</param>
    /// <param name="ProductId">The Product the group is, when it is one: only a Product has records of its own.</param>
    private sealed record Group(string Key, string Name, Func<Post, bool> Holds, Guid? ProductId = null);

    private sealed class Recorded(IReadOnlyList<Post> posts, IReadOnlyList<CommissionRecord> records, LabDashboardOptions options)
    {
        private readonly ILookup<Guid, Post> _carrying = posts.Where(p => p.LinkId is not null).ToLookup(p => p.LinkId!.Value);
        private readonly ILookup<Guid, CommissionRecord> _ofLink =
            records.Where(r => r.AffiliateLinkId is not null).ToLookup(r => r.AffiliateLinkId!.Value);

        public LabDashboardGroup Row(Group group)
        {
            var members = posts.Where(group.Holds).ToList();
            var views = Metric(members, PerformanceMetric.Views);
            var clicks = Metric(members, PerformanceMetric.Clicks);

            // A link's Commission belongs to every Published Post that carries it together, so it is
            // the group's only when all of them are in the group. Otherwise it is left out, never divided.
            var links = members
                .Where(p => p.LinkId is not null)
                .Select(p => p.LinkId!.Value)
                .Distinct()
                .Where(link => _carrying[link].All(group.Holds))
                .ToList();
            var ofLinks = links.SelectMany(link => _ofLink[link]).ToList();
            // The link may have been in use before any Published Post here carried it.
            var beforePublication = links.Sum(link =>
            {
                var first = _carrying[link].Min(p => p.PublishedOn);
                return _ofLink[link].Count(r => r.PeriodStart < first);
            });

            return new LabDashboardGroup(
                group.Key,
                group.Name,
                members.Count,
                members.Count < options.MinimumPublishedPosts || views.Value is null || views.Value < options.MinimumViews,
                views,
                Metric(members, PerformanceMetric.Likes),
                Metric(members, PerformanceMetric.Comments),
                Metric(members, PerformanceMetric.Shares),
                clicks,
                views.Value is > 0 && clicks.Value is { } clicked ? decimal.Divide(clicked, views.Value.Value) : null,
                ConversionRate(members, clicks, links, beforePublication),
                Commissions.Totals(ofLinks),
                // Kept apart from the links': a report for the Product may already hold what a report for one of its links holds.
                Commissions.Totals(records.Where(r => group.ProductId is { } product && r.ProductId == product)),
                beforePublication);
        }

        // Orders for each click, from the links alone: an order recorded for a Product may have come from
        // anywhere. It is a rate only when every click in it could have led to an order in it, and the reverse.
        private decimal? ConversionRate(IReadOnlyList<Post> members, LabMetric clicks, IReadOnlyList<Guid> links, int beforePublication)
        {
            if (clicks.Value is not > 0 || beforePublication > 0) return null;
            if (!members.All(p => p.LinkId is { } link && links.Contains(link))) return null;

            long orders = 0;
            foreach (var link in links)
            {
                var ofLink = _ofLink[link].ToList();
                if (ofLink.Count == 0 || ofLink.Any(r => r.Orders is null)) return null;
                orders += ofLink.Sum(r => (long)r.Orders!.Value);
            }
            return decimal.Divide(orders, clicks.Value.Value);
        }

        private static LabMetric Metric(IReadOnlyList<Post> members, PerformanceMetric metric)
        {
            var known = members.Where(p => p.Latest?.Total(metric) is not null).Select(p => p.Latest!).ToList();
            return new LabMetric(
                members.Count > 0 && known.Count == members.Count ? known.Sum(s => s.Total(metric)!.Value) : null,
                members.Count - known.Count,
                [.. known.Select(s => s.Source).Distinct().Order()],
                known.Count == 0 ? null : known.Min(s => s.TakenAt),
                known.Count == 0 ? null : known.Max(s => s.TakenAt));
        }
    }
}
