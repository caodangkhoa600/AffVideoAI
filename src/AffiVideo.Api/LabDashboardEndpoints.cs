using AffiVideo.Application.Lab;
using AffiVideo.Contracts;

namespace AffiVideo.Api;

// Part of the Affiliate Lab: it is mapped on the group MapLab returns.
internal static class LabDashboardEndpoints
{
    // Read only, and only of the caller's Organization. Nothing in it ranks one group above another.
    public static void MapLabDashboard(this RouteGroupBuilder lab) => lab.MapGet("/dashboard", async (
            ILabDashboard dashboard, CancellationToken cancellationToken) =>
        {
            var found = await dashboard.ReadAsync(cancellationToken);
            return TypedResults.Ok(new LabDashboardResponse(
                found.MinimumPublishedPosts,
                found.MinimumViews,
                found.Products.Select(ToResponse).ToList(),
                found.CreativeTemplates.Select(ToResponse).ToList(),
                found.Hooks.Select(ToResponse).ToList(),
                found.Campaigns.Select(ToResponse).ToList()));
        })
        .WithTags("Affiliate Lab")
        .WithName("GetLabDashboard")
        .WithSummary(
            "The Organization's Published Posts grouped by Product, creative template, Hook and Campaign: what their " +
            "latest Performance Snapshots add up to, the rates that can be worked out, and the Commission recorded at a " +
            "level that matches the group. A figure that is not known is null, never zero, and a group below the " +
            "minimums is marked as too small to compare.");

    private static LabDashboardGroupResponse ToResponse(LabDashboardGroup group) => new(
        group.Key,
        group.Name,
        group.PublishedPosts,
        group.TooSmallToCompare,
        ToResponse(group.Views),
        ToResponse(group.Likes),
        ToResponse(group.Comments),
        ToResponse(group.Shares),
        ToResponse(group.Clicks),
        group.ClickThroughRate,
        group.ConversionRate,
        CommissionEndpoints.ToResponse(group.RecordedForLinks),
        CommissionEndpoints.ToResponse(group.RecordedForProduct),
        group.RecordsBeforePublication);

    private static LabDashboardMetricResponse ToResponse(LabMetric metric) =>
        new(metric.Value, metric.Unknown, metric.Sources, metric.EarliestTakenAt, metric.LatestTakenAt);
}
