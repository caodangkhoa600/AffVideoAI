using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

/// <summary>The Affiliate Lab flag, and what the Lab keeps about a Product.</summary>
public sealed class AffiliateLabTests(AffiVideoApp app)
{
    internal const string Lab = "/api/v1/lab";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_demonstration_Organization_has_the_Affiliate_Lab_and_a_new_Organization_does_not()
    {
        using var demonstration = await app.SignedInAsync(new Credentials("owner@demo.affivideo.local", "demo-owner-password"));
        using var plain = await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

        Assert.True((await demonstration.GetAsync<SessionResponse>("/api/v1/session")).Organization.AffiliateLabEnabled);
        Assert.Equal(HttpStatusCode.OK, (await demonstration.GetAsync($"{Lab}/campaigns")).StatusCode);
        Assert.False((await plain.GetAsync<SessionResponse>("/api/v1/session")).Organization.AffiliateLabEnabled);
    }

    [Fact]
    public async Task Every_Lab_endpoint_refuses_an_Organization_without_the_Affiliate_Lab()
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        var variant = await StoryboardTests.NewVariantAsync(member, product.Id);
        var nothing = Guid.NewGuid();

        HttpResponseMessage[] responses =
        [
            await member.GetAsync($"{Lab}/products"),
            await member.GetAsync($"{Lab}/products/{product.Id}"),
            await member.PutAsync($"{Lab}/products/{product.Id}", new LabProductRequest(Shortlisted: true)),
            await member.GetAsync($"{Lab}/campaigns"),
            await member.PostAsync($"{Lab}/campaigns", new CampaignRequest("Tết")),
            // Refused before the body is read: a body that would be a 400 is still a 404.
            await member.PostAsync($"{Lab}/campaigns", new { }),
            await member.GetAsync($"{Lab}/campaigns/{nothing}"),
            await member.PutAsync($"{Lab}/campaigns/{nothing}", new CampaignRequest("Tết")),
            await member.PostAsync($"{Lab}/campaigns/{nothing}/archive", new { }),
            await member.GetAsync($"{Lab}/campaigns/{nothing}/variants"),
            await member.PutAsync($"{Lab}/campaigns/{nothing}/variants/{variant.Id}", new { }),
            await member.DeleteAsync($"{Lab}/campaigns/{nothing}/variants/{variant.Id}"),
            await member.GetAsync($"{Lab}/social-accounts"),
            await member.PostAsync($"{Lab}/social-accounts", new SocialAccountRequest(SocialPlatform.TikTok, "@lumo.vn")),
            await member.GetAsync($"{Lab}/affiliate-links"),
            await member.PostAsync($"{Lab}/affiliate-links", new AffiliateLinkRequest("https://s.shopee.vn/lumo")),
            await member.GetAsync($"{Lab}/published-posts"),
            await member.GetAsync($"{Lab}/published-posts/{nothing}"),
            await member.PostAsync($"{Lab}/published-posts", new PublishedPostRequest(
                nothing, nothing, new DateOnly(2026, 10, 12), "https://www.tiktok.com/@lumo/video/1")),
            await member.GetAsync($"{Lab}/published-posts/{nothing}/performance-snapshots"),
            await member.PostAsync($"{Lab}/published-posts/{nothing}/performance-snapshots", new { }),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));

        // The same member is let in from the moment the Organization is given the Lab, with no new session.
        await app.EnableAffiliateLabAsync(organization.Id);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"{Lab}/campaigns")).StatusCode);
        Assert.False((await member.GetAsync<LabProductResponse>($"{Lab}/products/{product.Id}")).Shortlisted);
    }

    [Fact]
    public async Task The_Lab_is_refused_to_someone_who_is_not_signed_in()
    {
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync($"{Lab}/campaigns");
        var create = await stranger.PostAsync($"{Lab}/campaigns", new CampaignRequest("Tết"));

        Assert.All([list, create], response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task A_Lab_member_shortlists_a_Product_with_research_notes_and_a_commission_rate()
    {
        using var member = await SignedInToLabAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Lumo 500"));
        var other = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "AirBeat X1"));

        var changed = await member.PutAsync($"{Lab}/products/{product.Id}", new LabProductRequest(
            Shortlisted: true, ResearchNotes: "  Bán chạy trên sàn, hoa hồng cao.  ", CommissionRatePercent: 12.5m));

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var read = await member.GetAsync<LabProductResponse>($"{Lab}/products/{product.Id}");
        Assert.Equal(product.Id, read.ProductId);
        Assert.Equal("Lumo 500", read.Name);
        Assert.True(read.Shortlisted);
        Assert.Equal("Bán chạy trên sàn, hoa hồng cao.", read.ResearchNotes);
        Assert.Equal(12.5m, read.CommissionRatePercent);
        Assert.Null(read.CommissionAmount);
        Assert.Null(read.CommissionCurrency);

        var shortlist = await member.GetAsync<PagedResponse<LabProductResponse>>($"{Lab}/products?shortlisted=true");
        Assert.Equal([product.Id], shortlist.Items.Select(p => p.ProductId));
        var rest = await member.GetAsync<PagedResponse<LabProductResponse>>($"{Lab}/products?shortlisted=false");
        Assert.Equal([other.Id], rest.Items.Select(p => p.ProductId));
        var all = await member.GetAsync<PagedResponse<LabProductResponse>>($"{Lab}/products");
        Assert.Equal([other.Id, product.Id], all.Items.Select(p => p.ProductId));
    }

    [Fact]
    public async Task What_the_Lab_keeps_is_on_the_shared_Product_and_changes_nothing_else_about_it()
    {
        using var member = await SignedInToLabAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Lumo 500"));
        var before = await member.GetAsync<ProductResponse>($"/api/v1/products/{product.Id}");

        (await member.PutAsync($"{Lab}/products/{product.Id}", new LabProductRequest(
            Shortlisted: true, ResearchNotes: "Hoa hồng cố định", CommissionAmount: 25000m, CommissionCurrency: "VND"))).EnsureSuccessStatusCode();
        var shortlisted = await member.GetAsync<ProductResponse>($"/api/v1/products/{product.Id}");
        // Editing the Product as any member does leaves what the Lab keeps.
        (await member.PutAsync($"/api/v1/products/{product.Id}", ProductTests.Valid(name: "Lumo 500 Pro"))).EnsureSuccessStatusCode();

        var after = await member.GetAsync<ProductResponse>($"/api/v1/products/{product.Id}");
        Assert.Equal(before with { UpdatedAt = shortlisted.UpdatedAt, Tags = shortlisted.Tags }, shortlisted);
        Assert.Equal(before.Tags, shortlisted.Tags);
        Assert.Equal("Lumo 500 Pro", after.Name);
        var lab = await member.GetAsync<LabProductResponse>($"{Lab}/products/{product.Id}");
        Assert.Equal("Lumo 500 Pro", lab.Name);
        Assert.True(lab.Shortlisted);
        Assert.Equal("Hoa hồng cố định", lab.ResearchNotes);
        Assert.Null(lab.CommissionRatePercent);
        Assert.Equal((25000m, "VND"), (lab.CommissionAmount, lab.CommissionCurrency));
        Assert.Equal(1, (await member.GetAsync<PagedResponse<ProductResponse>>("/api/v1/products")).Total);
    }

    [Fact]
    public async Task A_Product_is_taken_off_the_shortlist_and_keeps_its_notes()
    {
        using var member = await SignedInToLabAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        var path = $"{Lab}/products/{product.Id}";
        (await member.PutAsync(path, new LabProductRequest(Shortlisted: true, ResearchNotes: "Đã thử"))).EnsureSuccessStatusCode();

        (await member.PutAsync(path, new LabProductRequest(Shortlisted: false, ResearchNotes: "Đã thử"))).EnsureSuccessStatusCode();

        var read = await member.GetAsync<LabProductResponse>(path);
        Assert.False(read.Shortlisted);
        Assert.Equal("Đã thử", read.ResearchNotes);
        Assert.Empty((await member.GetAsync<PagedResponse<LabProductResponse>>($"{Lab}/products?shortlisted=true")).Items);
    }

    [Theory]
    [InlineData("""{"shortlisted":true,"commissionRatePercent":-1}""", "commissionRatePercent")]
    [InlineData("""{"shortlisted":true,"commissionRatePercent":100.01}""", "commissionRatePercent")]
    [InlineData("""{"shortlisted":true,"commissionRatePercent":12.345}""", "commissionRatePercent")]
    [InlineData("""{"shortlisted":true,"commissionAmount":25000}""", "commissionCurrency")]
    [InlineData("""{"shortlisted":true,"commissionCurrency":"VND"}""", "commissionAmount")]
    [InlineData("""{"shortlisted":true,"commissionAmount":-5,"commissionCurrency":"VND"}""", "commissionAmount")]
    [InlineData("""{"shortlisted":true,"commissionAmount":5,"commissionCurrency":"vnd"}""", "commissionCurrency")]
    // A rate or an amount, not both.
    [InlineData("""{"shortlisted":true,"commissionRatePercent":10,"commissionAmount":5,"commissionCurrency":"VND"}""", "commissionAmount")]
    public async Task A_commission_that_cannot_be_kept_is_refused_with_the_field(string body, string field)
    {
        using var member = await SignedInToLabAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        var path = $"{Lab}/products/{product.Id}";

        var response = await SendJsonAsync(member, HttpMethod.Put, path, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(AffiVideoApp.Json, Cancellation))!;
        Assert.Equal([field], problem.Errors.Keys);
        Assert.False((await member.GetAsync<LabProductResponse>(path)).Shortlisted);
    }

    [Fact]
    public async Task Research_notes_over_the_limit_and_a_Product_that_does_not_exist_are_refused()
    {
        using var member = await SignedInToLabAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());

        var tooLong = await member.PutAsync($"{Lab}/products/{product.Id}", new LabProductRequest(true, new string('n', 4001)));
        var longest = await member.PutAsync($"{Lab}/products/{product.Id}", new LabProductRequest(true, new string('n', 4000)));
        var missing = await member.PutAsync($"{Lab}/products/{Guid.NewGuid()}", new LabProductRequest(true));
        var readMissing = await member.GetAsync($"{Lab}/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readMissing.StatusCode);
    }

    internal static async Task<Browser> SignedInToLabAsync(AffiVideoApp app) =>
        await app.SignedInAsync((await app.CreateOrganizationAsync(affiliateLab: true)).Owner);

    private Task<Browser> SignedInToLabAsync() => SignedInToLabAsync(app);

    // A body the typed requests cannot express.
    internal static async Task<HttpResponseMessage> SendJsonAsync(Browser member, HttpMethod method, string path, string body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Browser.AntiforgeryHeader, await member.AntiforgeryTokenAsync());
        return await member.Http.SendAsync(request, Cancellation);
    }
}
