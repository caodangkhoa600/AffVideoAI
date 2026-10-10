using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>
/// The Published Posts of the caller's Organization, grouped four ways, with what
/// their latest Performance Snapshots add up to and the Commission that can
/// honestly be shown for each group. Nothing here calls one group better than another.
/// </summary>
public interface ILabDashboard
{
    Task<LabDashboard> ReadAsync(CancellationToken cancellationToken);
}

/// <param name="MinimumPublishedPosts">A group with fewer Published Posts is too small to compare.</param>
/// <param name="MinimumViews">A group with fewer views, or views that are not all known, is too small to compare.</param>
/// <param name="Products">Each Product that has a Published Post or a Commission record, by name.</param>
/// <param name="Campaigns">Every Campaign, by name. A Variant in two Campaigns counts in both, so their figures are not added together.</param>
public sealed record LabDashboard(
    int MinimumPublishedPosts,
    long MinimumViews,
    IReadOnlyList<LabDashboardGroup> Products,
    IReadOnlyList<LabDashboardGroup> CreativeTemplates,
    IReadOnlyList<LabDashboardGroup> Hooks,
    IReadOnlyList<LabDashboardGroup> Campaigns);

/// <summary>One row: the Published Posts of a Product, a creative template, a Hook or a Campaign.</summary>
/// <param name="Key">What tells the group from the others of its kind.</param>
/// <param name="TooSmallToCompare">Whether the whole row, its Commission included, is too little to set beside another row.</param>
/// <param name="ClickThroughRate">Clicks for each view. Null unless every Published Post knows both.</param>
/// <param name="ConversionRate">
/// Orders for each click. Null unless every Published Post knows its clicks and carries an affiliate link
/// that belongs wholly to the group, every such link has records that all say their orders, and none of
/// those records covers days before the link's first Published Post. Orders recorded for a Product never
/// make a rate: they include sales no click here led to.
/// </param>
/// <param name="RecordedForLinks">
/// What is recorded for the affiliate links whose every Published Post is in the group.
/// </param>
/// <param name="RecordedForProduct">
/// What is recorded for the Product itself. Empty for every group that is not a Product. Never added to
/// <paramref name="RecordedForLinks"/>.
/// </param>
/// <param name="RecordsBeforePublication">
/// How many of the links' records cover days before the first Published Post that carries the link.
/// </param>
public sealed record LabDashboardGroup(
    string Key,
    string Name,
    int PublishedPosts,
    bool TooSmallToCompare,
    LabMetric Views,
    LabMetric Likes,
    LabMetric Comments,
    LabMetric Shares,
    LabMetric Clicks,
    decimal? ClickThroughRate,
    decimal? ConversionRate,
    IReadOnlyList<CommissionTotal> RecordedForLinks,
    IReadOnlyList<CommissionTotal> RecordedForProduct,
    int RecordsBeforePublication);

/// <summary>One figure of a group, added up from the latest Performance Snapshot of each of its Published Posts.</summary>
/// <param name="Value">Null unless every Published Post knows the figure, which is unknown and not zero.</param>
/// <param name="Unknown">How many of the Published Posts do not know it, whether for want of a snapshot or of the figure.</param>
/// <param name="Sources">How the snapshots used got in.</param>
/// <param name="EarliestTakenAt">The earliest moment any snapshot used applies to. Null when none was used.</param>
/// <param name="LatestTakenAt">The latest moment any snapshot used applies to.</param>
public sealed record LabMetric(
    long? Value, int Unknown, IReadOnlyList<PerformanceSource> Sources, DateTimeOffset? EarliestTakenAt, DateTimeOffset? LatestTakenAt);
