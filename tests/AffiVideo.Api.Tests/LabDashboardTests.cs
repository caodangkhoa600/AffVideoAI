using System.Net;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The Affiliate Lab dashboard, read for one seeded Lab Organization that these tests
/// share and none of them changes: seven Published Posts of three Products, their
/// Performance Snapshots, and Commission records at both levels.
/// </summary>
public sealed class LabDashboardTests(AffiVideoApp app)
{
    private const string Dashboard = $"{AffiliateLabTests.Lab}/dashboard";

    private static readonly DateTimeOffset Tuesday = new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Figures_are_totalled_by_Product_from_the_latest_snapshot_of_each_Published_Post()
    {
        var (seeded, dashboard) = await ReadAsync();

        var earbuds = Assert.Single(dashboard.Products, g => g.Key == seeded.Earbuds.ProductId.ToString());

        Assert.Equal(("Tai nghe AirBeat X1", 3, false), (earbuds.Name, earbuds.PublishedPosts, earbuds.TooSmallToCompare));
        // The first Published Post has an older snapshot of 100 views: only its latest, of 200, counts.
        Assert.Equal(
            (1000L, 100L, 10L, 10L, 60L),
            (earbuds.Views.Value!.Value, earbuds.Likes.Value!.Value, earbuds.Comments.Value!.Value, earbuds.Shares.Value!.Value,
                earbuds.Clicks.Value!.Value));
        Assert.Equal(0.06m, earbuds.ClickThroughRate);
        // Every figure says where it came from: how its snapshots got in, and the moments they apply to.
        Assert.Equal([PerformanceSource.Manual], earbuds.Views.Sources);
        Assert.Equal((0, Tuesday, Tuesday), (earbuds.Views.Unknown, earbuds.Views.EarliestTakenAt, earbuds.Views.LatestTakenAt));
        Assert.Equal(["Bình giữ nhiệt Lumo 500", "Quạt mini Breeze", "Tai nghe AirBeat X1"], dashboard.Products.Select(g => g.Name));
    }

    [Fact]
    public async Task A_figure_one_Published_Post_does_not_know_is_unknown_for_the_group_never_zero()
    {
        var (seeded, dashboard) = await ReadAsync();

        var flask = Assert.Single(dashboard.Products, g => g.Key == seeded.Flask.ProductId.ToString());
        var fan = Assert.Single(dashboard.Products, g => g.Key == seeded.Fan.ProductId.ToString());

        // One of the flask's two Published Posts has no snapshot at all, and the other knows only views and likes.
        Assert.Equal(2, flask.PublishedPosts);
        Assert.Equal(((long?)null, 1), (flask.Views.Value, flask.Views.Unknown));
        Assert.Equal(((long?)null, 2), (flask.Clicks.Value, flask.Clicks.Unknown));
        Assert.Null(flask.ClickThroughRate);
        Assert.Null(flask.ConversionRate);
        // What is known of it still says where it came from.
        Assert.Equal([PerformanceSource.Manual], flask.Views.Sources);
        Assert.Empty(flask.Clicks.Sources);
        Assert.Null(flask.Clicks.EarliestTakenAt);
        // The fan knows its views and clicks, so it has a rate, but one of its two does not know its likes.
        Assert.Equal((70L, 6L), (fan.Views.Value!.Value, fan.Clicks.Value!.Value));
        Assert.Equal(decimal.Divide(6, 70), fan.ClickThroughRate);
        Assert.Equal(((long?)null, 1), (fan.Likes.Value, fan.Likes.Unknown));
        using var member = await app.SignedInAsync(seeded.Owner);
        var raw = await (await member.GetAsync(Dashboard)).Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("\"clicks\":{\"value\":null,\"unknown\":2", raw);
    }

    [Fact]
    public async Task A_group_below_the_minimum_Published_Posts_or_views_is_too_small_to_compare()
    {
        var (seeded, dashboard) = await ReadAsync();

        Assert.Equal((2, 100L), (dashboard.MinimumPublishedPosts, dashboard.MinimumViews));
        // Two Published Posts, but 70 views between them.
        Assert.True(Assert.Single(dashboard.Products, g => g.Key == seeded.Fan.ProductId.ToString()).TooSmallToCompare);
        // Views that are not all known cannot be said to reach the minimum.
        Assert.True(Assert.Single(dashboard.Products, g => g.Key == seeded.Flask.ProductId.ToString()).TooSmallToCompare);
        // A Campaign with no Published Post is listed, and is too small.
        var empty = Assert.Single(dashboard.Campaigns, g => g.Key == seeded.Empty.Id.ToString());
        Assert.Equal((0, true, (long?)null, 0), (empty.PublishedPosts, empty.TooSmallToCompare, empty.Views.Value, empty.Views.Unknown));
        Assert.Empty(empty.RecordedForLinks);
        Assert.False(Assert.Single(dashboard.Products, g => g.Key == seeded.Earbuds.ProductId.ToString()).TooSmallToCompare);
        // The mark is on the row, so on its Commission too: the fan's row has Commission and is still too small.
        Assert.NotEmpty(Assert.Single(dashboard.Products, g => g.Key == seeded.Fan.ProductId.ToString()).RecordedForLinks);
    }

    [Fact]
    public async Task A_Product_shows_what_is_recorded_for_it_and_for_its_links_as_two_figures()
    {
        var (seeded, dashboard) = await ReadAsync();

        var earbuds = Assert.Single(dashboard.Products, g => g.Key == seeded.Earbuds.ProductId.ToString());
        var flask = Assert.Single(dashboard.Products, g => g.Key == seeded.Flask.ProductId.ToString());

        var forLinks = Assert.Single(earbuds.RecordedForLinks);
        Assert.Equal(("VND", 390m, 30m, 10m, 370m), (forLinks.Currency, forLinks.Commission, forLinks.Refunds, forLinks.Adjustments, forLinks.Net));
        Assert.Equal((12, 2), (forLinks.Orders, forLinks.Records));
        var forProduct = Assert.Single(earbuds.RecordedForProduct);
        Assert.Equal((500m, 40, 1), (forProduct.Net, forProduct.Orders, forProduct.Records));
        // Each says the report it was read from and how it got in.
        Assert.Equal(["Shopee Affiliate"], forLinks.Reports);
        Assert.Equal(["Shopee, by Product"], forProduct.Reports);
        Assert.Equal([CommissionSource.Manual], forProduct.Sources);
        // The flask's only link is also carried by a Published Post of the fan, so it counts for neither Product.
        Assert.Empty(flask.RecordedForLinks);
        Assert.Empty(flask.RecordedForProduct);
        var fan = Assert.Single(dashboard.Products, g => g.Key == seeded.Fan.ProductId.ToString());
        Assert.Equal(20m, Assert.Single(fan.RecordedForLinks).Commission);
    }

    [Fact]
    public async Task Conversion_is_orders_for_each_click_from_links_that_belong_wholly_to_the_group()
    {
        var (seeded, dashboard) = await ReadAsync();

        var earbuds = Assert.Single(dashboard.Products, g => g.Key == seeded.Earbuds.ProductId.ToString());
        var fan = Assert.Single(dashboard.Products, g => g.Key == seeded.Fan.ProductId.ToString());
        var onlyEarbuds = Assert.Single(dashboard.Campaigns, g => g.Key == seeded.OnlyEarbuds.Id.ToString());
        var earbudsAndFan = Assert.Single(dashboard.Campaigns, g => g.Key == seeded.EarbudsAndFan.Id.ToString());

        // Twelve orders on its links for sixty clicks. The forty orders recorded for the Product make no rate:
        // they include sales that no click here led to.
        Assert.Equal(0.2m, earbuds.ConversionRate);
        Assert.Equal(0.2m, onlyEarbuds.ConversionRate);
        // One of the fan's Published Posts carries a link the flask shares, so its orders cannot be matched to these clicks.
        Assert.Null(fan.ConversionRate);
        Assert.Null(earbudsAndFan.ConversionRate);
    }

    [Fact]
    public async Task Records_from_before_a_link_was_published_count_in_Commission_marked_and_make_no_rate()
    {
        var (seeded, dashboard) = await ReadAsync();

        var fan = Assert.Single(dashboard.Hooks, g => g.Key == seeded.Fan.Hook);
        var earbuds = Assert.Single(dashboard.Hooks, g => g.Key == seeded.Earbuds.Hook);

        // The fan's own link has a record from the first of October, and its Published Post went up on the sixth.
        Assert.Equal(1, fan.RecordsBeforePublication);
        Assert.Equal(20m, Assert.Single(fan.RecordedForLinks).Commission);
        Assert.Null(fan.ConversionRate);
        Assert.Equal(0, earbuds.RecordsBeforePublication);
    }

    [Fact]
    public async Task Commission_for_a_creative_template_a_Hook_and_a_Campaign_is_of_the_links_wholly_inside_it()
    {
        var (seeded, dashboard) = await ReadAsync();

        var template = Assert.Single(dashboard.CreativeTemplates);
        var hook = Assert.Single(dashboard.Hooks, g => g.Key == seeded.Earbuds.Hook);
        var onlyEarbuds = Assert.Single(dashboard.Campaigns, g => g.Key == seeded.OnlyEarbuds.Id.ToString());
        var earbudsAndFan = Assert.Single(dashboard.Campaigns, g => g.Key == seeded.EarbudsAndFan.Id.ToString());

        // Every Published Post has the one creative template, so every link is wholly inside it.
        Assert.Equal((nameof(CreativeTemplate.ProductShowcase), 7), (template.Key, template.PublishedPosts));
        Assert.Equal(((long?)null, 1), (template.Views.Value, template.Views.Unknown));
        var ofTemplate = Assert.Single(template.RecordedForLinks);
        Assert.Equal((480m, 460m, 15, 4), (ofTemplate.Commission, ofTemplate.Net, ofTemplate.Orders, ofTemplate.Records));
        Assert.Null(template.ConversionRate);
        // Only a Product has records of its own: what is recorded for the earbuds is not the Hook's or the Campaign's.
        Assert.Equal((3, 1000L, 0.2m), (hook.PublishedPosts, hook.Views.Value!.Value, hook.ConversionRate!.Value));
        Assert.Equal(370m, Assert.Single(hook.RecordedForLinks).Net);
        Assert.All([template, hook, onlyEarbuds, earbudsAndFan], group => Assert.Empty(group.RecordedForProduct));
        Assert.Equal(370m, Assert.Single(onlyEarbuds.RecordedForLinks).Net);
        // The Campaign of the earbuds and the fan holds the fan's own link, but not the one the flask shares.
        Assert.Equal((5, 1070L, 66L), (earbudsAndFan.PublishedPosts, earbudsAndFan.Views.Value!.Value, earbudsAndFan.Clicks.Value!.Value));
        Assert.Equal((410m, 3), (Assert.Single(earbudsAndFan.RecordedForLinks).Commission, Assert.Single(earbudsAndFan.RecordedForLinks).Records));
        Assert.Equal(["Earbuds and fan", "Empty", "Only earbuds"], dashboard.Campaigns.Select(g => g.Name));
    }

    [Fact]
    public async Task Nothing_is_called_a_winner_or_better_than_another_group()
    {
        var (seeded, _) = await ReadAsync();
        using var member = await app.SignedInAsync(seeded.Owner);

        var raw = await (await member.GetAsync(Dashboard)).Content.ReadAsStringAsync(Cancellation);

        Assert.All(
            new[] { "winner", "best", "better", "rank", "top" },
            word => Assert.DoesNotContain($"\"{word}", raw, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_dashboard_is_empty_for_a_new_Lab_and_holds_nothing_of_another_Organization()
    {
        var lab = await app.CreateOrganizationAsync(affiliateLab: true);
        using var member = await app.SignedInAsync(lab.Owner);
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());

        var dashboard = await member.GetAsync<LabDashboardResponse>(Dashboard);

        // A Product with no Published Post and no Commission record has nothing to show.
        Assert.Empty(dashboard.Products);
        Assert.Empty(dashboard.CreativeTemplates);
        Assert.Empty(dashboard.Hooks);
        Assert.Empty(dashboard.Campaigns);
        // It is listed once Commission is recorded for it, with no Published Post.
        await CommissionRecordTests.RecordedAsync(member, CommissionRecordTests.Valid(productId: product.Id, commission: 55m));
        var withCommission = Assert.Single((await member.GetAsync<LabDashboardResponse>(Dashboard)).Products);
        Assert.Equal((product.Id.ToString(), 0, true), (withCommission.Key, withCommission.PublishedPosts, withCommission.TooSmallToCompare));
        Assert.Equal(55m, Assert.Single(withCommission.RecordedForProduct).Net);
    }

    private async Task<(Seeded Seeded, LabDashboardResponse Dashboard)> ReadAsync()
    {
        var seeded = await SeededAsync();
        using var member = await app.SignedInAsync(seeded.Owner);
        return (seeded, await member.GetAsync<LabDashboardResponse>(Dashboard));
    }

    // Earbuds: three Published Posts, one with a link of its own and two sharing a link, every figure known.
    // Flask: two, one sharing a link with a fan's and knowing only views and likes, one with no link and no snapshot.
    // Fan: two, one on the link the flask shares, one with a link of its own that earned before it was published.
    private Task<Seeded> SeededAsync() => app.OnceAsync(async () =>
    {
        var organization = await app.CreateOrganizationAsync(affiliateLab: true);
        using var member = await app.SignedInAsync(organization.Owner);
        var earbuds = await RenderedVideoTests.RenderedAsync(app, member, "Tai nghe AirBeat X1", "Bấm xem thêm", "Bạn còn nghe nhạc bằng tai nghe dây?");
        var flask = await RenderedVideoTests.RenderedAsync(app, member, "Bình giữ nhiệt Lumo 500", "Đặt mua bình trong hôm nay", "Bạn vẫn uống nước ấm lạnh ngắt?");
        var fan = await RenderedVideoTests.RenderedAsync(app, member, "Quạt mini Breeze", "Xem quạt ngay", "Nóng quá chịu không nổi?");
        foreach (var video in new[] { earbuds, flask, fan })
        {
            (await RenderedVideoTests.ApproveAsync(member, video.Id)).EnsureSuccessStatusCode();
        }

        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        var ownOfEarbuds = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var sharedByEarbuds = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var sharedByFlaskAndFan = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var ownOfFan = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        Task<PublishedPostResponse> Post(RenderedVideoResponse video, int day, Guid? link) =>
            PublishedPostTests.RecordedAsync(member, video.Id, account.Id, new DateOnly(2026, 10, day), affiliateLinkId: link);
        Task Snapshot(PublishedPostResponse post, PerformanceSnapshotRequest totals) =>
            PerformanceSnapshotTests.RecordedAsync(member, post.Id, totals);

        var first = await Post(earbuds, 1, ownOfEarbuds.Id);
        await Snapshot(first, new PerformanceSnapshotRequest(Tuesday.AddDays(-1), Views: 100, Likes: 10, Comments: 2, Shares: 3, Clicks: 5));
        await Snapshot(first, new PerformanceSnapshotRequest(Tuesday, Views: 200, Likes: 20, Comments: 4, Shares: 6, Clicks: 10));
        await Snapshot(await Post(earbuds, 2, sharedByEarbuds.Id), new PerformanceSnapshotRequest(Tuesday, Views: 300, Likes: 30, Comments: 6, Shares: 3, Clicks: 20));
        await Snapshot(await Post(earbuds, 3, sharedByEarbuds.Id), new PerformanceSnapshotRequest(Tuesday, Views: 500, Likes: 50, Comments: 0, Shares: 1, Clicks: 30));
        await Snapshot(await Post(flask, 4, sharedByFlaskAndFan.Id), new PerformanceSnapshotRequest(Tuesday, Views: 50, Likes: 5));
        await Post(flask, 4, null);
        await Snapshot(await Post(fan, 5, sharedByFlaskAndFan.Id), new PerformanceSnapshotRequest(Tuesday, Views: 40, Likes: 4, Comments: 1, Shares: 0, Clicks: 4));
        await Snapshot(await Post(fan, 6, ownOfFan.Id), new PerformanceSnapshotRequest(Tuesday, Views: 30, Comments: 0, Shares: 0, Clicks: 2));

        Task Commission(CommissionRecordRequest record) => CommissionRecordTests.RecordedAsync(member, record);
        await Commission(CommissionRecordTests.Valid(affiliateLinkId: ownOfEarbuds.Id, commission: 90m, orders: 3));
        // From the second, the day the first of the two Published Posts that share the link went up.
        await Commission(CommissionRecordTests.Valid(
            affiliateLinkId: sharedByEarbuds.Id, commission: 300m, refunds: 30m, adjustments: 10m, orders: 9, periodStart: new DateOnly(2026, 10, 2)));
        await Commission(CommissionRecordTests.Valid(productId: earbuds.ProductId, commission: 500m, orders: 40, report: "Shopee, by Product"));
        await Commission(CommissionRecordTests.Valid(
            affiliateLinkId: sharedByFlaskAndFan.Id, commission: 70m, orders: 2, periodStart: new DateOnly(2026, 10, 8)));
        // From the first of October, five days before the fan's Published Post carried the link.
        await Commission(CommissionRecordTests.Valid(affiliateLinkId: ownOfFan.Id, commission: 20m, orders: 1));

        // The earbuds' Variant is in two Campaigns.
        var onlyEarbuds = await CampaignTests.CreateAsync(member, "Only earbuds");
        var earbudsAndFan = await CampaignTests.CreateAsync(member, "Earbuds and fan");
        var empty = await CampaignTests.CreateAsync(member, "Empty");
        foreach (var (campaign, video) in new[] { (onlyEarbuds, earbuds), (earbudsAndFan, earbuds), (earbudsAndFan, fan) })
        {
            (await member.PutAsync($"{CampaignTests.Campaigns}/{campaign.Id}/variants/{video.VariantId}", new { })).EnsureSuccessStatusCode();
        }

        return new Seeded(organization.Owner, earbuds, flask, fan, onlyEarbuds, earbudsAndFan, empty);
    });

    private sealed record Seeded(
        Credentials Owner, RenderedVideoResponse Earbuds, RenderedVideoResponse Flask, RenderedVideoResponse Fan,
        CampaignResponse OnlyEarbuds, CampaignResponse EarbudsAndFan, CampaignResponse Empty);
}
