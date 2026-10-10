using AffiVideo.Application.Lab;
using AffiVideo.Contracts;

namespace AffiVideo.Api;

internal static class LabDashboardEndpoints
{
    public static void MapLabDashboard(this RouteGroupBuilder lab) => lab.MapGet("/dashboard", async (
            ILabDashboard dashboard, CancellationToken cancellationToken) =>
        {
            var found = await dashboard.ReadAsync(cancellationToken);
            return TypedResults.Ok(new LabDashboardResponse(
                found.MinimumPosts, found.MinimumViews,
                found.Products.Select(ToResponse).ToList(), found.CreativeTemplates.Select(ToResponse).ToList(),
                found.Hooks.Select(ToResponse).ToList(), found.Campaigns.Select(ToResponse).ToList()));
        })
        .WithTags("Affiliate Lab")
        .WithName("GetLabDashboard")
        .WithSummary("Latest Published Post performance grouped by Product, creative template, Hook and Campaign. Unknown figures stay unknown; Commission is attributed only where its record permits.");

    private static LabDashboardGroupResponse ToResponse(LabDashboardGroup group) => new(
        group.Key, group.Name, group.Posts, group.TooSmallToCompare,
        ToResponse(group.Views), ToResponse(group.Likes), ToResponse(group.Comments),
        ToResponse(group.Shares), ToResponse(group.Clicks), group.ClickThroughRate, group.ConversionRate,
        group.Commission.Select(CommissionEndpoints.ToResponse).ToList(),
        group.PostSources, group.ClickThroughRateSources, group.ConversionRateSources);

    private static LabDashboardMetricResponse ToResponse(LabMetric metric) => new(metric.Value, metric.Sources);
}
