using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

public interface ILabDashboard
{
    Task<LabDashboard> ReadAsync(CancellationToken cancellationToken);
}

public sealed record LabDashboard(
    int MinimumPosts, long MinimumViews,
    IReadOnlyList<LabDashboardGroup> Products,
    IReadOnlyList<LabDashboardGroup> CreativeTemplates,
    IReadOnlyList<LabDashboardGroup> Hooks,
    IReadOnlyList<LabDashboardGroup> Campaigns);

public sealed record LabDashboardGroup(
    string Key, string Name, int Posts, bool TooSmallToCompare,
    LabMetric Views, LabMetric Likes, LabMetric Comments, LabMetric Shares, LabMetric Clicks,
    decimal? ClickThroughRate, decimal? ConversionRate,
    IReadOnlyList<CommissionTotal> Commission,
    IReadOnlyList<string> PostSources,
    IReadOnlyList<string> ClickThroughRateSources,
    IReadOnlyList<string> ConversionRateSources);

/// <summary>Unknown if any post has not reported the figure; sources name the latest snapshots used.</summary>
public sealed record LabMetric(long? Value, IReadOnlyList<string> Sources);
