using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Commission records, and where their Commission is shown. The tests share the Lab
/// Organization of <see cref="PublishedPostTests"/>; each uses affiliate links of
/// its own, and a currency of its own where it reads what a shared Product adds up to.
/// </summary>
public sealed class CommissionRecordTests(AffiVideoApp app)
{
    internal const string Records = $"{AffiliateLabTests.Lab}/commission-records";

    private static readonly DateOnly FirstOfOctober = new(2026, 10, 1);
    private static readonly DateOnly SeventhOfOctober = new(2026, 10, 7);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static string LinkCommission(Guid linkId) => $"{PublishedPostTests.Links}/{linkId}/commission";

    internal static string ProductCommission(Guid productId) => $"{AffiliateLabTests.Lab}/products/{productId}/commission";

    [Fact]
    public async Task A_Commission_record_is_recorded_for_an_affiliate_link_and_read_back()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var before = DateTimeOffset.UtcNow;

        var response = await member.PostAsync(Records, new CommissionRecordRequest(
            FirstOfOctober, SeventhOfOctober, "  Shopee Affiliate  ", "VND", 350000m, AffiliateLinkId: link.Id,
            Orders: 12, ConfirmedOrders: 9, Refunds: 40000m, Adjustments: 10000m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var record = await ReadAsync<CommissionRecordResponse>(response);
        Assert.Equal((link.Id, (Guid?)null), (record.AffiliateLinkId, record.ProductId));
        Assert.Equal((FirstOfOctober, SeventhOfOctober), (record.PeriodStart, record.PeriodEnd));
        Assert.Equal(("Shopee Affiliate", "VND"), (record.Source, record.Currency));
        Assert.Equal((12, 9), (record.Orders, record.ConfirmedOrders));
        Assert.Equal((350000m, 40000m, 10000m, 300000m), (record.Commission, record.Refunds, record.Adjustments, record.Net));
        Assert.InRange(record.RecordedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        var listed = await ListAsync(member, $"?affiliateLinkId={link.Id}");
        Assert.Equal(1, listed.Total);
        Assert.Equal(record, Assert.Single(listed.Items));
    }

    [Fact]
    public async Task A_Commission_record_is_attached_to_a_Product_when_that_is_the_level_the_report_gives()
    {
        using var member = await LabMemberAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Đèn bàn Lumo"));
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        var record = await RecordedAsync(member, Valid(productId: product.Id));
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id));

        Assert.Equal(((Guid?)null, product.Id), (record.AffiliateLinkId, record.ProductId));
        Assert.Equal([record.Id], (await ListAsync(member, $"?productId={product.Id}")).Items.Select(r => r.Id));
        var total = Assert.Single((await member.GetAsync<ProductCommissionResponse>(ProductCommission(product.Id))).Totals);
        Assert.Equal((record.Net, 1), (total.Net, total.Records));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task A_Commission_record_is_attached_to_exactly_one_affiliate_link_or_one_Product(bool toLink, bool toProduct)
    {
        using var member = await LabMemberAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        var response = await member.PostAsync(
            Records, Valid(affiliateLinkId: toLink ? link.Id : null, productId: toProduct ? product.Id : null));

        Assert.Equal(["affiliateLinkId"], await PublishedPostTests.RefusedFieldsAsync(response));
        Assert.Equal(0, (await ListAsync(member, $"?affiliateLinkId={link.Id}")).Total);
        Assert.Equal(0, (await ListAsync(member, $"?productId={product.Id}")).Total);
    }

    [Theory]
    [InlineData("""{"periodStart":"2026-10-08","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10}""", "periodEnd")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":" ","currency":"VND","commission":10}""", "source")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"vnd","commission":10}""", "currency")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":-1}""", "commission")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10.005}""", "commission")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10,"refunds":-5}""", "refunds")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10,"adjustments":-5}""", "adjustments")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10,"orders":-1}""", "orders")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10,"confirmedOrders":-1}""", "confirmedOrders")]
    public async Task A_Commission_record_needs_a_period_a_source_a_currency_and_amounts_of_zero_or_more(string body, string refusedField)
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        var response = await AffiliateLabTests.SendJsonAsync(
            member, HttpMethod.Post, Records, body.Insert(1, $"\"affiliateLinkId\":\"{link.Id}\","));

        Assert.Equal([refusedField], await PublishedPostTests.RefusedFieldsAsync(response));
        Assert.Equal(0, (await ListAsync(member, $"?affiliateLinkId={link.Id}")).Total);
    }

    [Theory]
    // No currency, no Commission, no period: an amount is never kept without what it is an amount of.
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","commission":10}""")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND"}""")]
    [InlineData("""{"periodStart":"2026-10-01","source":"Shopee","currency":"VND","commission":10}""")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":"a lot"}""")]
    [InlineData("""{"periodStart":"2026-10-01","periodEnd":"2026-10-07","source":"Shopee","currency":"VND","commission":10,"orders":1.5}""")]
    public async Task A_Commission_record_that_cannot_be_read_is_refused(string body)
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        var response = await AffiliateLabTests.SendJsonAsync(
            member, HttpMethod.Post, Records, body.Insert(1, $"\"affiliateLinkId\":\"{link.Id}\","));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await ListAsync(member, $"?affiliateLinkId={link.Id}")).Total);
    }

    [Fact]
    public async Task A_Commission_record_for_an_affiliate_link_or_a_Product_there_is_not_is_refused()
    {
        using var member = await LabMemberAsync();

        var forNoLink = await member.PostAsync(Records, Valid(affiliateLinkId: Guid.NewGuid()));
        var forNoProduct = await member.PostAsync(Records, Valid(productId: Guid.NewGuid()));

        Assert.Equal(["affiliateLinkId"], await PublishedPostTests.RefusedFieldsAsync(forNoLink));
        Assert.Equal(["productId"], await PublishedPostTests.RefusedFieldsAsync(forNoProduct));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(LinkCommission(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(ProductCommission(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Amounts_are_kept_as_decimals_each_with_its_currency_and_currencies_are_never_added_together()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        // 0.1 + 0.2 is not 0.3 in binary floating point. It is here.
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, currency: "USD", commission: 0.1m));
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, currency: "USD", commission: 0.2m, periodStart: new DateOnly(2026, 10, 8)));
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, currency: "VND", commission: 9876543210123.45m));

        var totals = (await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals;
        Assert.Equal([("USD", 0.3m, 2), ("VND", 9876543210123.45m, 1)], totals.Select(t => (t.Currency, t.Commission, t.Records)));
        var raw = await (await member.GetAsync($"{Records}?affiliateLinkId={link.Id}")).Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("\"commission\":9876543210123.45", raw);
        Assert.Contains("\"currency\":\"VND\"", raw);
    }

    [Fact]
    public async Task Refunds_and_adjustments_are_recorded_and_reduce_the_net_figure()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());

        var first = await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 500m, refunds: 120.5m, adjustments: 30m));
        var plain = await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 200m, periodStart: new DateOnly(2026, 10, 8)));
        // More was taken back in a period than was earned in it: the net figure says so.
        var later = await RecordedAsync(
            member, Valid(affiliateLinkId: link.Id, commission: 0m, refunds: 75m, periodStart: new DateOnly(2026, 10, 15)));

        Assert.Equal((349.5m, 200m, -75m), (first.Net, plain.Net, later.Net));
        Assert.Equal((0m, 0m), (plain.Refunds, plain.Adjustments));
        var total = Assert.Single((await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals);
        Assert.Equal((700m, 195.5m, 30m, 474.5m), (total.Commission, total.Refunds, total.Adjustments, total.Net));
    }

    [Fact]
    public async Task Orders_the_report_does_not_give_are_unknown_not_zero()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, orders: 5, confirmedOrders: 0));

        var knowsBoth = Assert.Single((await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals);
        var withoutConfirmed = await RecordedAsync(
            member, Valid(affiliateLinkId: link.Id, orders: 3, periodStart: new DateOnly(2026, 10, 8)));

        Assert.Equal((5, 0), (knowsBoth.Orders, knowsBoth.ConfirmedOrders));
        Assert.Null(withoutConfirmed.ConfirmedOrders);
        // A total is only as known as its least known record.
        var total = Assert.Single((await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals);
        Assert.Equal(8, total.Orders);
        Assert.Null(total.ConfirmedOrders);
        var raw = await (await member.GetAsync(LinkCommission(link.Id))).Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("\"confirmedOrders\":null", raw);
    }

    [Fact]
    public async Task The_same_report_for_the_same_period_is_recorded_once()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var other = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 100m));

        var again = await member.PostAsync(Records, Valid(affiliateLinkId: link.Id, commission: 250m));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(
            "A Commission record from this source, in this currency, for this period is already there. Delete it first if its figures are wrong.",
            (await ReadAsync<ProblemDetails>(again)).Detail);
        // Another period, another source, another currency and another link are each another report.
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, periodStart: new DateOnly(2026, 10, 2)));
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, source: "Lazada Affiliate"));
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, currency: "USD"));
        await RecordedAsync(member, Valid(affiliateLinkId: other.Id));
        Assert.Equal(4, (await ListAsync(member, $"?affiliateLinkId={link.Id}")).Total);
    }

    [Fact]
    public async Task Commission_records_are_listed_latest_period_first()
    {
        using var member = await LabMemberAsync();
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var second = await RecordedAsync(
            member, Valid(affiliateLinkId: link.Id, periodStart: new DateOnly(2026, 10, 8), periodEnd: new DateOnly(2026, 10, 14)));
        var first = await RecordedAsync(member, Valid(affiliateLinkId: link.Id));
        var third = await RecordedAsync(
            member, Valid(affiliateLinkId: link.Id, periodStart: new DateOnly(2026, 10, 15), periodEnd: new DateOnly(2026, 10, 21)));

        var listed = await ListAsync(member, $"?affiliateLinkId={link.Id}");
        var secondPage = await ListAsync(member, $"?affiliateLinkId={link.Id}&page=2&pageSize=2");

        Assert.Equal([third.Id, second.Id, first.Id], listed.Items.Select(r => r.Id));
        Assert.Equal([first.Id], secondPage.Items.Select(r => r.Id));
        Assert.Equal(3, secondPage.Total);
        // What they add up to says which days it covers.
        var total = Assert.Single((await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals);
        Assert.Equal((FirstOfOctober, new DateOnly(2026, 10, 21)), (total.PeriodStart, total.PeriodEnd));
    }

    [Fact]
    public async Task A_wrong_Commission_record_is_deleted_and_the_deletion_is_in_the_audit_log()
    {
        var lab = await PublishedPostTests.LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var wrong = await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 9999m));
        var kept = await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 80m, periodStart: new DateOnly(2026, 10, 8)));

        var deleted = await member.DeleteAsync($"{Records}/{wrong.Id}");
        var again = await member.DeleteAsync($"{Records}/{wrong.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal([kept.Id], (await ListAsync(member, $"?affiliateLinkId={link.Id}")).Items.Select(r => r.Id));
        Assert.Equal(80m, Assert.Single((await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id))).Totals).Net);
        // The period is free again for the right figures.
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 99.99m));
        var log = await member.GetAsync<PagedResponse<AuditLogEntryResponse>>(
            $"/api/v1/organizations/{lab.OrganizationId}/audit-log?pageSize=200");
        Assert.Single(log.Items, entry => entry.Action == "commission-record.deleted" && entry.SubjectId == wrong.Id);
    }

    [Fact]
    public async Task A_Published_Post_shows_Commission_when_no_other_Published_Post_carries_its_affiliate_link()
    {
        var lab = await PublishedPostTests.LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var post = await PublishedPostTests.RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 1), affiliateLinkId: link.Id);
        var withoutLink = await PublishedPostTests.RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 2));
        // Its link is its own, and nothing is recorded for it yet: that is no Commission so far, not unknown.
        Assert.Equal([], post.Commission);

        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, commission: 300m, refunds: 50m, orders: 6, confirmedOrders: 5));
        await RecordedAsync(
            member, Valid(affiliateLinkId: link.Id, commission: 100m, orders: 2, confirmedOrders: 2, periodStart: new DateOnly(2026, 10, 8),
                periodEnd: new DateOnly(2026, 10, 14)));

        var read = await member.GetAsync<PublishedPostResponse>($"{PublishedPostTests.Posts}/{post.Id}");
        var total = Assert.Single(read.Commission!);
        Assert.Equal(("VND", 400m, 50m, 0m, 350m), (total.Currency, total.Commission, total.Refunds, total.Adjustments, total.Net));
        Assert.Equal((8, 7, 2), (total.Orders, total.ConfirmedOrders, total.Records));
        Assert.Equal(["Shopee Affiliate"], total.Sources);
        Assert.Equal((FirstOfOctober, new DateOnly(2026, 10, 14)), (total.PeriodStart, total.PeriodEnd));
        // The list of Published Posts carries the same figure.
        var listed = (await PublishedPostTests.ListAsync(member, $"?socialAccountId={account.Id}")).Items;
        Assert.Equal(350m, Assert.Single(Assert.Single(listed, p => p.Id == post.Id).Commission!).Net);
        // A Published Post with no affiliate link has nothing Commission could be recorded for.
        Assert.Null(withoutLink.Commission);
        Assert.Null(Assert.Single(listed, p => p.Id == withoutLink.Id).Commission);
    }

    [Fact]
    public async Task Commission_for_a_shared_affiliate_link_is_shown_for_the_link_and_the_Product_and_never_split_by_post()
    {
        const string currency = "QAA";
        var lab = await PublishedPostTests.LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        var link = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var first = await PublishedPostTests.RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 1), affiliateLinkId: link.Id);
        await RecordedAsync(member, Valid(affiliateLinkId: link.Id, currency: currency, commission: 900m, refunds: 100m, orders: 10));
        Assert.Equal(800m, Assert.Single((await PostAsync(member, first.Id)).Commission!).Net);

        // A second Published Post of the same Product carries the link: what it earned is no longer the first one's alone.
        var second = await PublishedPostTests.RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 2), affiliateLinkId: link.Id);
        // One post has nine times the views and all of the clicks. That decides nothing about the Commission.
        var monday = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        await PerformanceSnapshotTests.RecordedAsync(member, first.Id, new PerformanceSnapshotRequest(monday, Views: 9000, Clicks: 300));
        await PerformanceSnapshotTests.RecordedAsync(member, second.Id, new PerformanceSnapshotRequest(monday, Views: 1000, Clicks: 0));

        Assert.Null(second.Commission);
        Assert.Null((await PostAsync(member, first.Id)).Commission);
        Assert.Null((await PostAsync(member, second.Id)).Commission);
        Assert.All(
            (await PublishedPostTests.ListAsync(member, $"?socialAccountId={account.Id}")).Items,
            post => Assert.Null(post.Commission));
        // It is shown whole for the link, which says how many Published Posts it belongs to...
        var forLink = await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id));
        Assert.Equal((link.Id, link.Url, 2), (forLink.AffiliateLinkId, forLink.Url, forLink.PublishedPostCount));
        var linkTotal = Assert.Single(forLink.Totals);
        Assert.Equal((currency, 900m, 100m, 800m, 10), (linkTotal.Currency, linkTotal.Commission, linkTotal.Refunds, linkTotal.Net, linkTotal.Orders));
        // ...and whole for the Product both of them are of.
        Assert.Equivalent(linkTotal, await ProductTotalAsync(member, lab.Earbuds.ProductId, currency), strict: true);
        Assert.Null(await ProductTotalAsync(member, lab.Flask.ProductId, currency));

        // Once a Published Post of another Product carries it too, it says nothing about either Product alone.
        await PublishedPostTests.RecordedAsync(member, lab.Flask.Id, account.Id, new DateOnly(2026, 10, 3), affiliateLinkId: link.Id);

        Assert.Null(await ProductTotalAsync(member, lab.Earbuds.ProductId, currency));
        Assert.Null(await ProductTotalAsync(member, lab.Flask.ProductId, currency));
        var stillForLink = await member.GetAsync<LinkCommissionResponse>(LinkCommission(link.Id));
        Assert.Equal((3, 800m), (stillForLink.PublishedPostCount, Assert.Single(stillForLink.Totals).Net));
    }

    [Fact]
    public async Task What_a_Product_earned_is_its_own_records_and_those_of_the_affiliate_links_only_its_Published_Posts_carry()
    {
        const string currency = "QAB";
        var lab = await PublishedPostTests.LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        var carried = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        var unused = await PublishedPostTests.LinkAsync(member, PublishedPostTests.NewUrl());
        await PublishedPostTests.RecordedAsync(member, lab.Flask.Id, account.Id, new DateOnly(2026, 10, 1), affiliateLinkId: carried.Id);

        await RecordedAsync(member, Valid(productId: lab.Flask.ProductId, currency: currency, commission: 100m, source: "Lazada Affiliate"));
        await RecordedAsync(member, Valid(affiliateLinkId: carried.Id, currency: currency, commission: 50m, refunds: 5m));
        // No Published Post carries this link, so nothing says which Product it sold.
        await RecordedAsync(member, Valid(affiliateLinkId: unused.Id, currency: currency, commission: 7m));

        var total = await ProductTotalAsync(member, lab.Flask.ProductId, currency);
        Assert.NotNull(total);
        Assert.Equal((150m, 5m, 145m, 2), (total.Commission, total.Refunds, total.Net, total.Records));
        Assert.Equal(["Lazada Affiliate", "Shopee Affiliate"], total.Sources);
        Assert.Null(await ProductTotalAsync(member, lab.Earbuds.ProductId, currency));
        // The list for a Product is of the records attached to the Product itself.
        Assert.Equal(
            [100m],
            (await ListAsync(member, $"?productId={lab.Flask.ProductId}&pageSize=200")).Items.Where(r => r.Currency == currency).Select(r => r.Commission));
    }

    private async Task<Browser> LabMemberAsync() => await app.SignedInAsync((await PublishedPostTests.LabAsync(app)).Owner);

    internal static CommissionRecordRequest Valid(
        Guid? affiliateLinkId = null, Guid? productId = null, string currency = "VND", decimal commission = 150000m,
        decimal refunds = 0, decimal adjustments = 0, int? orders = null, int? confirmedOrders = null,
        DateOnly? periodStart = null, DateOnly? periodEnd = null, string source = "Shopee Affiliate")
    {
        var start = periodStart ?? FirstOfOctober;
        return new CommissionRecordRequest(
            start, periodEnd ?? start.AddDays(6), source, currency, commission, affiliateLinkId, productId,
            orders, confirmedOrders, refunds, adjustments);
    }

    internal static async Task<CommissionRecordResponse> RecordedAsync(Browser member, CommissionRecordRequest request)
    {
        var response = await member.PostAsync(Records, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<CommissionRecordResponse>(response);
    }

    internal static Task<PagedResponse<CommissionRecordResponse>> ListAsync(Browser member, string query = "") =>
        member.GetAsync<PagedResponse<CommissionRecordResponse>>($"{Records}{query}");

    private static Task<PublishedPostResponse> PostAsync(Browser member, Guid postId) =>
        member.GetAsync<PublishedPostResponse>($"{PublishedPostTests.Posts}/{postId}");

    // The Products are shared by the tests: of what one adds up to, only this test's currency is its to judge.
    private static async Task<CommissionTotalResponse?> ProductTotalAsync(Browser member, Guid productId, string currency) =>
        (await member.GetAsync<ProductCommissionResponse>(ProductCommission(productId))).Totals.SingleOrDefault(t => t.Currency == currency);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
