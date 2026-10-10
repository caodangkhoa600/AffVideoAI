using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// The Organization's Published Posts grouped four ways. Nothing in it calls one group
/// better than another, and a figure that is not known is null, never zero.
/// </summary>
/// <param name="MinimumPublishedPosts">A group with fewer Published Posts is too small to compare.</param>
/// <param name="MinimumViews">A group with fewer views, or views that are not all known, is too small to compare.</param>
/// <param name="Products">Each Product that has a Published Post or a Commission record, by name.</param>
/// <param name="Campaigns">
/// Every Campaign, by name. A Variant in two Campaigns counts in both, so the figures of two Campaigns are not added together.
/// </param>
public sealed record LabDashboardResponse(
    int MinimumPublishedPosts,
    long MinimumViews,
    IReadOnlyList<LabDashboardGroupResponse> Products,
    IReadOnlyList<LabDashboardGroupResponse> CreativeTemplates,
    IReadOnlyList<LabDashboardGroupResponse> Hooks,
    IReadOnlyList<LabDashboardGroupResponse> Campaigns);

/// <summary>One row: the Published Posts of a Product, a creative template, a Hook or a Campaign.</summary>
/// <param name="Key">What tells the group from the others of its kind: an identifier, or the creative template or Hook itself.</param>
/// <param name="TooSmallToCompare">Whether the whole row, its Commission included, is too little to set beside another row.</param>
/// <param name="ClickThroughRate">Clicks for each view. Null, which is not available, unless every Published Post knows both.</param>
/// <param name="ConversionRate">
/// Orders for each click. Null, which is not available, unless every Published Post knows its clicks and carries an
/// affiliate link that belongs wholly to the group, every such link has records that all say their orders, and none
/// of those records covers days before the link's first Published Post. Orders recorded for a Product never make a rate.
/// </param>
/// <param name="RecordedForLinks">What is recorded for the affiliate links whose every Published Post is in the group.</param>
/// <param name="RecordedForProduct">
/// What is recorded for the Product itself. Empty for every group that is not a Product. Never added to what is recorded for the links.
/// </param>
/// <param name="RecordsBeforePublication">
/// How many of the links' records cover days before the first Published Post that carries the link.
/// </param>
public sealed record LabDashboardGroupResponse(
    string Key,
    string Name,
    int PublishedPosts,
    bool TooSmallToCompare,
    LabDashboardMetricResponse Views,
    LabDashboardMetricResponse Likes,
    LabDashboardMetricResponse Comments,
    LabDashboardMetricResponse Shares,
    LabDashboardMetricResponse Clicks,
    decimal? ClickThroughRate,
    decimal? ConversionRate,
    IReadOnlyList<CommissionTotalResponse> RecordedForLinks,
    IReadOnlyList<CommissionTotalResponse> RecordedForProduct,
    int RecordsBeforePublication);

/// <summary>One figure of a group, added up from the latest Performance Snapshot of each of its Published Posts.</summary>
/// <param name="Value">Null unless every Published Post knows the figure, which is unknown and not zero.</param>
/// <param name="Unknown">How many of the Published Posts do not know it.</param>
/// <param name="Sources">How the snapshots used got in.</param>
/// <param name="EarliestTakenAt">The earliest moment any snapshot used applies to. Null when none was used.</param>
/// <param name="LatestTakenAt">The latest moment any snapshot used applies to.</param>
public sealed record LabDashboardMetricResponse(
    long? Value, int Unknown, IReadOnlyList<PerformanceSource> Sources, DateTimeOffset? EarliestTakenAt, DateTimeOffset? LatestTakenAt);
