using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Social accounts, affiliate links, and the Published Posts recorded with them.
/// The tests that record a Published Post share one Lab Organization and its
/// Rendered Videos; each uses URLs and accounts of its own.
/// </summary>
public sealed class PublishedPostTests(AffiVideoApp app)
{
    internal const string Accounts = $"{AffiliateLabTests.Lab}/social-accounts";
    internal const string Links = $"{AffiliateLabTests.Lab}/affiliate-links";
    internal const string Posts = $"{AffiliateLabTests.Lab}/published-posts";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_social_account_is_created_once_and_listed_by_platform_and_handle()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var before = DateTimeOffset.UtcNow;

        var created = await member.PostAsync(Accounts, new SocialAccountRequest(SocialPlatform.TikTok, "  @lumo.vn  "));
        var sameAgain = await member.PostAsync(Accounts, new SocialAccountRequest(SocialPlatform.TikTok, "@lumo.vn"));
        var onAnotherPlatform = await member.PostAsync(Accounts, new SocialAccountRequest(SocialPlatform.Facebook, "@lumo.vn"));
        var another = await member.PostAsync(Accounts, new SocialAccountRequest(SocialPlatform.TikTok, "@airbeat"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await ReadAsync<SocialAccountResponse>(created);
        Assert.Equal((SocialPlatform.TikTok, "@lumo.vn"), (account.Platform, account.Handle));
        Assert.InRange(account.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(HttpStatusCode.Conflict, sameAgain.StatusCode);
        Assert.Equal("This social account is already recorded.", (await ReadAsync<ProblemDetails>(sameAgain)).Detail);
        Assert.Equal(HttpStatusCode.Created, onAnotherPlatform.StatusCode);
        Assert.Equal(HttpStatusCode.Created, another.StatusCode);
        var listed = await member.GetAsync<PagedResponse<SocialAccountResponse>>(Accounts);
        Assert.Equal(3, listed.Total);
        Assert.Equal(
            [(SocialPlatform.Facebook, "@lumo.vn"), (SocialPlatform.TikTok, "@airbeat"), (SocialPlatform.TikTok, "@lumo.vn")],
            listed.Items.Select(a => (a.Platform, a.Handle)));
    }

    [Theory]
    [InlineData("""{"platform":"TikTok","handle":"   "}""")]
    [InlineData("""{"platform":"TikTok"}""")]
    [InlineData("""{"handle":"@lumo.vn"}""")]
    [InlineData("""{"platform":"Zalo","handle":"@lumo.vn"}""")]
    [InlineData("""{"platform":7,"handle":"@lumo.vn"}""")]
    public async Task A_social_account_needs_a_platform_there_is_and_a_handle(string body)
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);

        var response = await AffiliateLabTests.SendJsonAsync(member, HttpMethod.Post, Accounts, body);
        var tooLong = await member.PostAsync(Accounts, new SocialAccountRequest(SocialPlatform.TikTok, new string('h', 101)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(0, (await member.GetAsync<PagedResponse<SocialAccountResponse>>(Accounts)).Total);
    }

    [Fact]
    public async Task An_affiliate_link_is_created_once_and_listed_newest_first()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);

        var created = await member.PostAsync(Links, new AffiliateLinkRequest("  https://s.shopee.vn/lumo-1  ", "  Lumo, video 1  "));
        var sameAgain = await member.PostAsync(Links, new AffiliateLinkRequest("https://s.shopee.vn/lumo-1"));
        var unnamed = await member.PostAsync(Links, new AffiliateLinkRequest("https://s.shopee.vn/lumo-2"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await ReadAsync<AffiliateLinkResponse>(created);
        Assert.Equal(("https://s.shopee.vn/lumo-1", "Lumo, video 1", 0), (link.Url, link.Label, link.PublishedPostCount));
        Assert.Equal(HttpStatusCode.Conflict, sameAgain.StatusCode);
        Assert.Equal("This affiliate link is already recorded.", (await ReadAsync<ProblemDetails>(sameAgain)).Detail);
        Assert.Equal(HttpStatusCode.Created, unnamed.StatusCode);
        var listed = await member.GetAsync<PagedResponse<AffiliateLinkResponse>>(Links);
        Assert.Equal(
            [("https://s.shopee.vn/lumo-2", ""), ("https://s.shopee.vn/lumo-1", "Lumo, video 1")],
            listed.Items.Select(l => (l.Url, l.Label)));
        Assert.Equal(2, listed.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("s.shopee.vn/lumo")]
    [InlineData("ftp://s.shopee.vn/lumo")]
    public async Task An_affiliate_link_needs_a_web_address(string url)
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);

        var response = await member.PostAsync(Links, new AffiliateLinkRequest(url));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(AffiVideoApp.Json, Cancellation))!;
        Assert.Equal(["url"], problem.Errors.Keys);
        Assert.Equal(0, (await member.GetAsync<PagedResponse<AffiliateLinkResponse>>(Links)).Total);
    }

    [Fact]
    public async Task A_Published_Post_is_recorded_for_an_approved_Rendered_Video_and_read_back()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var link = await LinkAsync(member, NewUrl(), "Tai nghe, video 1");
        var url = NewUrl();
        var before = DateTimeOffset.UtcNow;

        var response = await member.PostAsync(
            Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 12), $"  {url}  ", link.Id));
        var withoutLink = await member.PostAsync(
            Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 13), NewUrl()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await ReadAsync<PublishedPostResponse>(response);
        Assert.Equal($"{Posts}/{post.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(lab.Earbuds.Id, post.RenderedVideoId);
        Assert.Equal(
            (lab.Earbuds.ProductId, "Tai nghe AirBeat X1", lab.Earbuds.ProjectId, lab.Earbuds.VariantId),
            (post.ProductId, post.ProductName, post.ProjectId, post.VariantId));
        Assert.Equal((CreativeTemplate.ProductShowcase, "Bạn còn nghe nhạc bằng tai nghe dây?"), (post.CreativeTemplate, post.Hook));
        Assert.Equal(
            (account.Id, SocialPlatform.TikTok, account.Handle),
            (post.SocialAccount.Id, post.SocialAccount.Platform, post.SocialAccount.Handle));
        Assert.Equal(new DateOnly(2026, 10, 12), post.PublishedOn);
        Assert.Equal(url, post.Url);
        Assert.Equal(new PublishedPostLinkResponse(link.Id, link.Url, "Tai nghe, video 1"), post.AffiliateLink);
        Assert.False(post.AffiliateLinkShared);
        Assert.InRange(post.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(post, await member.GetAsync<PublishedPostResponse>($"{Posts}/{post.Id}"));
        // An affiliate link is optional.
        Assert.Equal(HttpStatusCode.Created, withoutLink.StatusCode);
        var plain = await ReadAsync<PublishedPostResponse>(withoutLink);
        Assert.Null(plain.AffiliateLink);
        Assert.False(plain.AffiliateLinkShared);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{Posts}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Recording_a_Published_Post_for_a_Rendered_Video_that_is_not_approved_is_refused()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());

        var response = await member.PostAsync(
            Posts, new PublishedPostRequest(lab.Waiting.Id, account.Id, new DateOnly(2026, 10, 12), NewUrl()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "This Rendered Video has not been approved. Only an approved Rendered Video can be recorded as a Published Post.",
            (await ReadAsync<ProblemDetails>(response)).Detail);
        Assert.Equal(0, (await ListAsync(member, $"?socialAccountId={account.Id}")).Total);
    }

    [Fact]
    public async Task The_same_Rendered_Video_on_three_accounts_is_three_Published_Posts()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var handle = NewHandle();
        SocialAccountResponse[] accounts =
        [
            await AccountAsync(member, SocialPlatform.TikTok, handle),
            await AccountAsync(member, SocialPlatform.Facebook, handle),
            await AccountAsync(member, SocialPlatform.YouTube, handle),
        ];

        var posts = new List<PublishedPostResponse>();
        foreach (var account in accounts)
        {
            posts.Add(await RecordedAsync(member, lab.Flask.Id, account.Id, new DateOnly(2026, 10, 12)));
        }

        Assert.Equal(3, posts.Select(p => p.Id).Distinct().Count());
        Assert.All(posts, post => Assert.Equal(lab.Flask.Id, post.RenderedVideoId));
        Assert.Equal(accounts.Select(a => a.Id), posts.Select(p => p.SocialAccount.Id));
        var ofTheVideo = await ListAsync(member, $"?variantId={lab.Flask.VariantId}");
        Assert.Equal(posts.Select(p => p.Id).Order(), Mine(ofTheVideo, posts).Order());
    }

    [Fact]
    public async Task The_same_URL_cannot_be_recorded_twice()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var other = await AccountAsync(member, SocialPlatform.Facebook, NewHandle());
        var url = NewUrl();
        var first = await RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 12), url);

        var again = await member.PostAsync(Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 12), url));
        // Whatever the video, account or day.
        var elsewhere = await member.PostAsync(Posts, new PublishedPostRequest(lab.Flask.Id, other.Id, new DateOnly(2026, 10, 14), $" {url} "));

        Assert.All([again, elsewhere], response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        Assert.Equal("This URL is already recorded as a Published Post.", (await ReadAsync<ProblemDetails>(again)).Detail);
        Assert.Equal([first.Id], (await ListAsync(member, $"?socialAccountId={account.Id}")).Items.Select(p => p.Id));
        Assert.Equal(0, (await ListAsync(member, $"?socialAccountId={other.Id}")).Total);
    }

    [Fact]
    public async Task A_Published_Post_needs_a_Rendered_Video_an_account_a_date_and_a_URL()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var day = new DateOnly(2026, 10, 12);
        var nothing = Guid.NewGuid();

        var noVideo = await member.PostAsync(Posts, new PublishedPostRequest(nothing, account.Id, day, NewUrl()));
        var noAccount = await member.PostAsync(Posts, new PublishedPostRequest(lab.Earbuds.Id, nothing, day, NewUrl()));
        var noLink = await member.PostAsync(Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, day, NewUrl(), nothing));
        var noneOfThem = await member.PostAsync(Posts, new PublishedPostRequest(nothing, nothing, day, NewUrl(), nothing));
        var noUrl = await member.PostAsync(Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, day, "  "));
        var notAnAddress = await member.PostAsync(Posts, new PublishedPostRequest(lab.Earbuds.Id, account.Id, day, "tiktok.com/video/1"));
        var noDate = await AffiliateLabTests.SendJsonAsync(member, HttpMethod.Post, Posts,
            $$"""{"renderedVideoId":"{{lab.Earbuds.Id}}","socialAccountId":"{{account.Id}}","url":"{{NewUrl()}}"}""");
        var noSuchDate = await AffiliateLabTests.SendJsonAsync(member, HttpMethod.Post, Posts,
            $$"""{"renderedVideoId":"{{lab.Earbuds.Id}}","socialAccountId":"{{account.Id}}","publishedOn":"2026-02-30","url":"{{NewUrl()}}"}""");

        Assert.Equal(["renderedVideoId"], await RefusedFieldsAsync(noVideo));
        Assert.Equal(["socialAccountId"], await RefusedFieldsAsync(noAccount));
        Assert.Equal(["affiliateLinkId"], await RefusedFieldsAsync(noLink));
        Assert.Equal(["affiliateLinkId", "renderedVideoId", "socialAccountId"], await RefusedFieldsAsync(noneOfThem));
        Assert.Equal(["url"], await RefusedFieldsAsync(noUrl));
        Assert.Equal(["url"], await RefusedFieldsAsync(notAnAddress));
        Assert.Equal(HttpStatusCode.BadRequest, noDate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noSuchDate.StatusCode);
        Assert.Equal(0, (await ListAsync(member, $"?socialAccountId={account.Id}")).Total);
    }

    [Fact]
    public async Task Published_Posts_are_listed_latest_first_and_narrowed_by_Campaign_Product_Variant_platform_and_account()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var tiktok = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var shopee = await AccountAsync(member, SocialPlatform.Shopee, NewHandle());
        var campaign = await CampaignTests.CreateAsync(member, "Tai nghe");
        (await CampaignTests.AddVariantAsync(member, campaign.Id, lab.Earbuds.VariantId)).EnsureSuccessStatusCode();
        var empty = await CampaignTests.CreateAsync(member, "Chưa có gì");
        var earbudsOnTikTok = await RecordedAsync(member, lab.Earbuds.Id, tiktok.Id, new DateOnly(2026, 10, 12));
        var flaskOnTikTok = await RecordedAsync(member, lab.Flask.Id, tiktok.Id, new DateOnly(2026, 10, 14));
        var earbudsOnShopee = await RecordedAsync(member, lab.Earbuds.Id, shopee.Id, new DateOnly(2026, 10, 13));
        PublishedPostResponse[] mine = [earbudsOnTikTok, flaskOnTikTok, earbudsOnShopee];

        Assert.Equal([flaskOnTikTok.Id, earbudsOnShopee.Id, earbudsOnTikTok.Id], Mine(await ListAsync(member, "?pageSize=200"), mine));
        Assert.Equal([earbudsOnShopee.Id, earbudsOnTikTok.Id], Mine(await ListAsync(member, $"?campaignId={campaign.Id}"), mine));
        Assert.Equal(0, (await ListAsync(member, $"?campaignId={empty.Id}")).Total);
        Assert.Equal(0, (await ListAsync(member, $"?campaignId={Guid.NewGuid()}")).Total);
        Assert.Equal([flaskOnTikTok.Id], Mine(await ListAsync(member, $"?productId={lab.Flask.ProductId}"), mine));
        Assert.Equal([earbudsOnShopee.Id, earbudsOnTikTok.Id], Mine(await ListAsync(member, $"?variantId={lab.Earbuds.VariantId}"), mine));
        Assert.Equal([flaskOnTikTok.Id, earbudsOnTikTok.Id], Mine(await ListAsync(member, "?platform=TikTok&pageSize=200"), mine));
        // These accounts are this test's own, so the lists are exactly these.
        var onTikTok = await ListAsync(member, $"?socialAccountId={tiktok.Id}");
        Assert.Equal([flaskOnTikTok.Id, earbudsOnTikTok.Id], onTikTok.Items.Select(p => p.Id));
        Assert.Equal(2, onTikTok.Total);
        Assert.Equal(
            [earbudsOnTikTok.Id],
            (await ListAsync(member, $"?socialAccountId={tiktok.Id}&productId={lab.Earbuds.ProductId}&platform=TikTok")).Items.Select(p => p.Id));
        Assert.Equal(0, (await ListAsync(member, $"?socialAccountId={tiktok.Id}&platform=Shopee")).Total);
        var secondPage = await ListAsync(member, $"?socialAccountId={tiktok.Id}&page=2&pageSize=1");
        Assert.Equal([earbudsOnTikTok.Id], secondPage.Items.Select(p => p.Id));
        Assert.Equal(2, secondPage.Total);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync($"{Posts}?platform=Zalo")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await member.GetAsync($"{Posts}?platform=7")).StatusCode);
    }

    [Fact]
    public async Task An_affiliate_link_says_how_many_Published_Posts_carry_it_and_a_post_says_when_its_link_is_shared()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var shared = await LinkAsync(member, NewUrl());
        var own = await LinkAsync(member, NewUrl());
        var first = await RecordedAsync(member, lab.Earbuds.Id, account.Id, new DateOnly(2026, 10, 12), affiliateLinkId: shared.Id);
        var alone = await RecordedAsync(member, lab.Flask.Id, account.Id, new DateOnly(2026, 10, 12), affiliateLinkId: own.Id);
        Assert.False(first.AffiliateLinkShared);
        // What the form reads before a link is chosen a second time.
        Assert.Equal(1, (await LinkAsync(member, shared.Id)).PublishedPostCount);

        var second = await RecordedAsync(member, lab.Flask.Id, account.Id, new DateOnly(2026, 10, 13), affiliateLinkId: shared.Id);

        Assert.True(second.AffiliateLinkShared);
        Assert.True((await member.GetAsync<PublishedPostResponse>($"{Posts}/{first.Id}")).AffiliateLinkShared);
        Assert.False((await member.GetAsync<PublishedPostResponse>($"{Posts}/{alone.Id}")).AffiliateLinkShared);
        Assert.Equal(
            [(second.Id, true), (alone.Id, false), (first.Id, true)],
            (await ListAsync(member, $"?socialAccountId={account.Id}")).Items.Select(p => (p.Id, p.AffiliateLinkShared)));
        Assert.Equal(2, (await LinkAsync(member, shared.Id)).PublishedPostCount);
        Assert.Equal(1, (await LinkAsync(member, own.Id)).PublishedPostCount);
    }

    [Fact]
    public async Task A_Rendered_Video_that_has_a_Published_Post_cannot_be_deleted()
    {
        var lab = await LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var video = await RenderedVideoTests.RenderedAsync(app, member, "Lumo 500 Mini");
        (await RenderedVideoTests.ApproveAsync(member, video.Id)).EnsureSuccessStatusCode();
        var account = await AccountAsync(member, SocialPlatform.TikTok, NewHandle());
        var post = await RecordedAsync(member, video.Id, account.Id, new DateOnly(2026, 10, 12));

        var deleted = await member.DeleteAsync($"{RenderedVideoTests.Videos}/{video.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal(
            "This Rendered Video has a Published Post, which is the record of where it is live. It cannot be deleted.",
            (await ReadAsync<ProblemDetails>(deleted)).Detail);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"{RenderedVideoTests.Videos}/{video.Id}/content")).StatusCode);
        Assert.Equal(post, await member.GetAsync<PublishedPostResponse>($"{Posts}/{post.Id}"));
        var log = await member.GetAsync<PagedResponse<AuditLogEntryResponse>>(
            $"/api/v1/organizations/{lab.OrganizationId}/audit-log?pageSize=200");
        Assert.DoesNotContain(log.Items, entry => entry.Action == "rendered-video.deleted" && entry.SubjectId == video.Id);
    }

    /// <summary>
    /// A Lab Organization with three Rendered Videos, for every test that records a
    /// Published Post: one of earbuds and one of a flask, both approved, and one
    /// still ready for review. The tests share it, so each uses accounts and URLs of its own.
    /// </summary>
    internal static Task<Lab> LabAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        var organization = await app.CreateOrganizationAsync(affiliateLab: true);
        using var member = await app.SignedInAsync(organization.Owner);
        var earbuds = await RenderedVideoTests.RenderedAsync(
            app, member, "Tai nghe AirBeat X1", "Bấm xem thêm", "Bạn còn nghe nhạc bằng tai nghe dây?");
        var flask = await RenderedVideoTests.RenderedAsync(
            app, member, "Bình giữ nhiệt Lumo 500", "Đặt mua bình trong hôm nay", "Bạn vẫn uống nước ấm lạnh ngắt?");
        var waiting = await RenderedVideoTests.RenderedAsync(app, member, "Lumo 500");
        (await RenderedVideoTests.ApproveAsync(member, earbuds.Id)).EnsureSuccessStatusCode();
        (await RenderedVideoTests.ApproveAsync(member, flask.Id)).EnsureSuccessStatusCode();
        return new Lab(organization.Owner, organization.Id, earbuds, flask, waiting);
    });

    /// <param name="Earbuds">Approved.</param>
    /// <param name="Flask">Approved, of another Product.</param>
    /// <param name="Waiting">Ready for review.</param>
    internal sealed record Lab(
        Credentials Owner, Guid OrganizationId, RenderedVideoResponse Earbuds, RenderedVideoResponse Flask, RenderedVideoResponse Waiting);

    /// <summary>A handle no other test has used.</summary>
    internal static string NewHandle() => $"@lumo-{Guid.NewGuid():N}";

    /// <summary>An address no other test has used.</summary>
    internal static string NewUrl() => $"https://www.tiktok.com/@lumo/video/{Guid.NewGuid():N}";

    internal static async Task<PublishedPostResponse> RecordedAsync(
        Browser member, Guid renderedVideoId, Guid socialAccountId, DateOnly publishedOn, string? url = null, Guid? affiliateLinkId = null)
    {
        var response = await member.PostAsync(
            Posts, new PublishedPostRequest(renderedVideoId, socialAccountId, publishedOn, url ?? NewUrl(), affiliateLinkId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PublishedPostResponse>(response);
    }

    internal static Task<PagedResponse<PublishedPostResponse>> ListAsync(Browser member, string query = "") =>
        member.GetAsync<PagedResponse<PublishedPostResponse>>($"{Posts}{query}");

    // The Organization is shared by the tests: of a list, only what this test recorded is its to judge.
    private static IEnumerable<Guid> Mine(PagedResponse<PublishedPostResponse> listed, IEnumerable<PublishedPostResponse> recorded)
    {
        var mine = recorded.Select(post => post.Id).ToHashSet();
        return listed.Items.Select(post => post.Id).Where(mine.Contains);
    }

    // The link as the list of links has it now.
    private static async Task<AffiliateLinkResponse> LinkAsync(Browser member, Guid linkId) =>
        Assert.Single((await member.GetAsync<PagedResponse<AffiliateLinkResponse>>($"{Links}?pageSize=200")).Items, link => link.Id == linkId);

    internal static async Task<IEnumerable<string>> RefusedFieldsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(AffiVideoApp.Json, Cancellation))!;
        return problem.Errors.Keys.Order();
    }

    internal static async Task<SocialAccountResponse> AccountAsync(Browser member, SocialPlatform platform, string handle)
    {
        var response = await member.PostAsync(Accounts, new SocialAccountRequest(platform, handle));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<SocialAccountResponse>(response);
    }

    internal static async Task<AffiliateLinkResponse> LinkAsync(Browser member, string url, string? label = null)
    {
        var response = await member.PostAsync(Links, new AffiliateLinkRequest(url, label));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<AffiliateLinkResponse>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
