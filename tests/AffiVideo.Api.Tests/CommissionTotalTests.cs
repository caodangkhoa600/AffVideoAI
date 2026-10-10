using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What Commission records add up to, as a pure function.</summary>
public sealed class CommissionTotalTests
{
    private static readonly Guid Link = Guid.NewGuid();

    [Fact]
    public void No_records_make_no_total()
    {
        Assert.Empty(Commissions.Totals([]));
    }

    [Fact]
    public void Records_are_added_up_by_currency_in_the_order_of_the_currency_codes()
    {
        var totals = Commissions.Totals(
        [
            Record("VND", new CommissionFigures(4, 3, 200_000m, 20_000m, -5_000m), day: 8, report: "Shopee Affiliate"),
            Record("USD", new CommissionFigures(1, 1, 12.34m, 0m, 0m), day: 1, report: "Amazon Associates"),
            Record("VND", new CommissionFigures(6, 6, 300_000m, 0m, 0m), day: 1, report: "Shopee Affiliate"),
            Record("VND", new CommissionFigures(0, 0, 0m, 30_000m, 2_000m), day: 15, report: "Lazada Affiliate"),
        ]);

        Assert.Equal(["USD", "VND"], totals.Select(total => total.Currency));
        var vnd = totals[1];
        Assert.Equal((10, 9), (vnd.Orders, vnd.ConfirmedOrders));
        // An adjustment below zero takes Commission away and one above adds it: 500,000 - 50,000 - 3,000.
        Assert.Equal((500_000m, 50_000m, -3_000m, 447_000m, 3), (vnd.Commission, vnd.Refunds, vnd.Adjustments, vnd.Net, vnd.Records));
        Assert.Equal(["Lazada Affiliate", "Shopee Affiliate"], vnd.Reports);
        Assert.Equal([CommissionSource.Manual], vnd.Sources);
        Assert.Equal(0, vnd.OverlappingRecords);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21)), (vnd.PeriodStart, vnd.PeriodEnd));
        Assert.Equal((12.34m, 12.34m, 1), (totals[0].Commission, totals[0].Net, totals[0].Records));
    }

    [Fact]
    public void A_count_of_orders_that_one_record_does_not_know_is_unknown_in_the_total()
    {
        var total = Assert.Single(Commissions.Totals(
        [
            Record("VND", new CommissionFigures(4, null, 100m, 0m, 0m), day: 1),
            Record("VND", new CommissionFigures(2, 2, 100m, 0m, 0m), day: 8),
        ]));

        Assert.Equal(6, total.Orders);
        Assert.Null(total.ConfirmedOrders);
    }

    [Fact]
    public void Records_for_the_same_link_and_currency_that_share_a_day_overlap_and_the_total_counts_them()
    {
        var week = Record("VND", new CommissionFigures(null, null, 100m, 0m, 0m), day: 1);
        var endsAsItStarts = Record("VND", new CommissionFigures(null, null, 100m, 0m, 0m), day: 7, report: "Monthly");
        var weekAfter = Record("VND", new CommissionFigures(null, null, 100m, 0m, 0m), day: 14);
        var inDollars = Record("USD", new CommissionFigures(null, null, 100m, 0m, 0m), day: 1);
        var ofAnotherLink = new CommissionRecord(
            Guid.NewGuid(), Guid.NewGuid(), new CommissionTarget(Guid.NewGuid(), null), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7),
            "Shopee Affiliate", "VND", new CommissionFigures(null, null, 100m, 0m, 0m), CommissionSource.Manual, DateTimeOffset.UnixEpoch);

        // The seventh is the last day of one and the first of the other.
        Assert.True(week.Overlaps(endsAsItStarts));
        Assert.True(endsAsItStarts.Overlaps(week));
        Assert.False(week.Overlaps(weekAfter));
        Assert.False(week.Overlaps(inDollars));
        Assert.False(week.Overlaps(ofAnotherLink));
        // A record does not overlap itself.
        Assert.False(week.Overlaps(week));
        var totals = Commissions.Totals([week, endsAsItStarts, weekAfter, inDollars]);
        Assert.Equal([("USD", 0), ("VND", 2)], totals.Select(total => (total.Currency, total.OverlappingRecords)));
    }

    [Fact]
    public void A_Commission_record_is_attached_to_one_affiliate_link_or_one_Product_and_its_period_runs_forwards()
    {
        var figures = new CommissionFigures(null, null, 1m, 0m, 0m);
        var day = new DateOnly(2026, 10, 1);

        Assert.Throws<ArgumentException>(() => New(new CommissionTarget(null, null), day, day));
        Assert.Throws<ArgumentException>(() => New(new CommissionTarget(Link, Guid.NewGuid()), day, day));
        Assert.Throws<ArgumentException>(() => New(new CommissionTarget(Link, null), day, day.AddDays(-1)));
        // A period of one day is a period.
        Assert.Equal(day, New(new CommissionTarget(null, Guid.NewGuid()), day, day).PeriodEnd);

        CommissionRecord New(CommissionTarget target, DateOnly start, DateOnly end) =>
            new(Guid.NewGuid(), Guid.NewGuid(), target, start, end, "Shopee Affiliate", "VND", figures, CommissionSource.Manual, DateTimeOffset.UnixEpoch);
    }

    // A week from the given day of October 2026.
    private static CommissionRecord Record(string currency, CommissionFigures figures, int day, string report = "Shopee Affiliate") =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), new CommissionTarget(Link, null), new DateOnly(2026, 10, day), new DateOnly(2026, 10, day + 6),
            report, currency, figures, CommissionSource.Manual, DateTimeOffset.UnixEpoch);
}
