namespace AffiVideo.Contracts;

public sealed record LabDashboardResponse(
    int MinimumPosts, long MinimumViews,
    IReadOnlyList<LabDashboardGroupResponse> Products,
    IReadOnlyList<LabDashboardGroupResponse> CreativeTemplates,
    IReadOnlyList<LabDashboardGroupResponse> Hooks,
    IReadOnlyList<LabDashboardGroupResponse> Campaigns);

public sealed record LabDashboardGroupResponse(
    string Key, string Name, int Posts, bool TooSmallToCompare,
    LabDashboardMetricResponse Views, LabDashboardMetricResponse Likes, LabDashboardMetricResponse Comments,
    LabDashboardMetricResponse Shares, LabDashboardMetricResponse Clicks,
    decimal? ClickThroughRate, decimal? ConversionRate,
    IReadOnlyList<CommissionTotalResponse> Commission,
    IReadOnlyList<string> PostSources,
    IReadOnlyList<string> ClickThroughRateSources,
    IReadOnlyList<string> ConversionRateSources);

public sealed record LabDashboardMetricResponse(long? Value, IReadOnlyList<string> Sources);
