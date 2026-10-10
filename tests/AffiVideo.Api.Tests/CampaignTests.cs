using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

public sealed class CampaignTests(AffiVideoApp app)
{
    internal const string Campaigns = $"{AffiliateLabTests.Lab}/campaigns";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_Lab_member_creates_a_Campaign_and_reads_it_back()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var before = DateTimeOffset.UtcNow;

        var created = await member.PostAsync(Campaigns, new CampaignRequest("  Thử nghiệm tháng 10  "));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var campaign = await ReadAsync<CampaignResponse>(created);
        Assert.Equal($"{Campaigns}/{campaign.Id}", created.Headers.Location?.OriginalString);
        var read = await member.GetAsync<CampaignResponse>($"{Campaigns}/{campaign.Id}");
        Assert.Equal("Thử nghiệm tháng 10", read.Name);
        Assert.Equal(CampaignStatus.Active, read.Status);
        Assert.Equal(0, read.VariantCount);
        Assert.InRange(read.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task A_Campaign_is_renamed_and_archived_and_the_list_is_newest_first_and_narrowed_by_status()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var first = await CreateAsync(member, "Thử nghiệm 1");
        var second = await CreateAsync(member, "Thử nghiệm 2");

        var renamed = await member.PutAsync($"{Campaigns}/{first.Id}", new CampaignRequest("  Tết 2027  "));
        var archived = await member.PostAsync($"{Campaigns}/{first.Id}/archive", new { });
        var archivedAgain = await member.PostAsync($"{Campaigns}/{first.Id}/archive", new { });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archivedAgain.StatusCode);
        var read = await member.GetAsync<CampaignResponse>($"{Campaigns}/{first.Id}");
        Assert.Equal(("Tết 2027", CampaignStatus.Archived), (read.Name, read.Status));
        Assert.Equal(read.UpdatedAt, (await ReadAsync<CampaignResponse>(archivedAgain)).UpdatedAt);

        Assert.Equal([second.Id, first.Id], (await ListAsync(member)).Items.Select(c => c.Id));
        Assert.Equal([second.Id], (await ListAsync(member, "?status=Active")).Items.Select(c => c.Id));
        Assert.Equal([first.Id], (await ListAsync(member, "?status=Archived")).Items.Select(c => c.Id));
        var page = await ListAsync(member, "?page=2&pageSize=1");
        Assert.Equal([first.Id], page.Items.Select(c => c.Id));
        Assert.Equal(2, page.Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Campaign_needs_a_name(string name)
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var campaign = await CreateAsync(member, "Thử nghiệm");

        var create = await member.PostAsync(Campaigns, new CampaignRequest(name));
        var tooLong = await member.PostAsync(Campaigns, new CampaignRequest(new string('c', 201)));
        var rename = await member.PutAsync($"{Campaigns}/{campaign.Id}", new CampaignRequest(name));

        Assert.All([create, tooLong, rename], response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(["Thử nghiệm"], (await ListAsync(member)).Items.Select(c => c.Name));
    }

    [Fact]
    public async Task Variants_from_different_Products_are_added_to_a_Campaign_and_listed_in_the_order_they_were_added()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var flask = await StoryboardTests.NewProductAsync(member, "Lumo 500");
        var earbuds = await StoryboardTests.NewProductAsync(member, "AirBeat X1");
        var flaskVariant = await StoryboardTests.NewVariantAsync(member, flask, hook: "Bạn vẫn dùng bình nhựa?");
        var earbudsVariant = await StoryboardTests.NewVariantAsync(
            member, earbuds, creativeTemplate: CreativeTemplate.LuxuryCinematic, hook: "Nghe nhạc không dây");
        var campaign = await CreateAsync(member, "Thử nghiệm");
        var other = await CreateAsync(member, "Khác");

        var added = await AddVariantAsync(member, campaign.Id, earbudsVariant.Id);
        var second = await AddVariantAsync(member, campaign.Id, flaskVariant.Id);
        var again = await AddVariantAsync(member, campaign.Id, earbudsVariant.Id);
        // A Variant can be in more than one Campaign.
        (await AddVariantAsync(member, other.Id, flaskVariant.Id)).EnsureSuccessStatusCode();

        Assert.All([added, second, again], response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        var variants = await VariantsAsync(member, campaign.Id);
        Assert.Equal(2, variants.Total);
        Assert.Equal(
            [
                (earbudsVariant.Id, earbudsVariant.ProjectId, earbuds, "AirBeat X1", CreativeTemplate.LuxuryCinematic, "Nghe nhạc không dây"),
                (flaskVariant.Id, flaskVariant.ProjectId, flask, "Lumo 500", CreativeTemplate.ProductShowcase, "Bạn vẫn dùng bình nhựa?"),
            ],
            variants.Items.Select(v => (v.VariantId, v.ProjectId, v.ProductId, v.ProductName, v.CreativeTemplate, v.Hook)));
        Assert.Equal(2, (await member.GetAsync<CampaignResponse>($"{Campaigns}/{campaign.Id}")).VariantCount);
        Assert.Equal([2, 1], (await ListAsync(member)).Items.OrderBy(c => c.CreatedAt).Select(c => c.VariantCount));
        Assert.Equal([flaskVariant.Id], (await VariantsAsync(member, campaign.Id, "?page=2&pageSize=1")).Items.Select(v => v.VariantId));
    }

    [Fact]
    public async Task Removing_a_Variant_from_a_Campaign_or_archiving_the_Campaign_does_not_delete_the_Variant()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var product = await StoryboardTests.NewProductAsync(member);
        var kept = await ReadVariantAsync(member, await StoryboardTests.NewVariantAsync(member, product, hook: "Giữ lạnh 24 giờ"));
        var removed = await ReadVariantAsync(member, await StoryboardTests.NewVariantAsync(member, product, hook: "Bạn vẫn dùng bình nhựa?"));
        var campaign = await CreateAsync(member, "Thử nghiệm");
        (await AddVariantAsync(member, campaign.Id, kept.Id)).EnsureSuccessStatusCode();
        (await AddVariantAsync(member, campaign.Id, removed.Id)).EnsureSuccessStatusCode();

        var remove = await member.DeleteAsync($"{Campaigns}/{campaign.Id}/variants/{removed.Id}");
        var removeAgain = await member.DeleteAsync($"{Campaigns}/{campaign.Id}/variants/{removed.Id}");
        (await member.PostAsync($"{Campaigns}/{campaign.Id}/archive", new { })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removeAgain.StatusCode);
        // The archived Campaign still groups what it grouped.
        Assert.Equal([kept.Id], (await VariantsAsync(member, campaign.Id)).Items.Select(v => v.VariantId));
        // Both Variants are as they were, under their own Projects.
        Assert.Equal(removed, await ReadVariantAsync(member, removed));
        Assert.Equal(kept, await ReadVariantAsync(member, kept));
    }

    [Fact]
    public async Task Deleting_a_Project_takes_its_Variants_out_of_the_Campaign_and_keeps_the_Campaign()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var product = await StoryboardTests.NewProductAsync(member);
        var gone = await StoryboardTests.NewVariantAsync(member, product);
        var stays = await StoryboardTests.NewVariantAsync(member, product);
        var campaign = await CreateAsync(member, "Thử nghiệm");
        (await AddVariantAsync(member, campaign.Id, gone.Id)).EnsureSuccessStatusCode();
        (await AddVariantAsync(member, campaign.Id, stays.Id)).EnsureSuccessStatusCode();

        var deleted = await member.DeleteAsync($"{ProjectTests.Projects}/{gone.ProjectId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal([stays.Id], (await VariantsAsync(member, campaign.Id)).Items.Select(v => v.VariantId));
    }

    [Fact]
    public async Task A_Campaign_or_a_Variant_that_does_not_exist_is_not_found()
    {
        using var member = await AffiliateLabTests.SignedInToLabAsync(app);
        var variant = await StoryboardTests.NewVariantAsync(member, await StoryboardTests.NewProductAsync(member));
        var campaign = await CreateAsync(member, "Thử nghiệm");
        var nothing = Guid.NewGuid();

        HttpResponseMessage[] responses =
        [
            await member.GetAsync($"{Campaigns}/{nothing}"),
            await member.PutAsync($"{Campaigns}/{nothing}", new CampaignRequest("Tết")),
            await member.PostAsync($"{Campaigns}/{nothing}/archive", new { }),
            await member.GetAsync($"{Campaigns}/{nothing}/variants"),
            await AddVariantAsync(member, nothing, variant.Id),
            await AddVariantAsync(member, campaign.Id, nothing),
            await member.DeleteAsync($"{Campaigns}/{campaign.Id}/variants/{variant.Id}"),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(0, (await VariantsAsync(member, campaign.Id)).Total);
    }

    // The Variant as its own Project's page reads it.
    private static Task<VariantResponse> ReadVariantAsync(Browser member, VariantResponse variant) =>
        member.GetAsync<VariantResponse>($"{VariantTests.Variants(variant.ProjectId)}/{variant.Id}");

    internal static async Task<CampaignResponse> CreateAsync(Browser member, string name)
    {
        var response = await member.PostAsync(Campaigns, new CampaignRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<CampaignResponse>(response);
    }

    internal static Task<HttpResponseMessage> AddVariantAsync(Browser member, Guid campaignId, Guid variantId) =>
        member.PutAsync($"{Campaigns}/{campaignId}/variants/{variantId}", new { });

    internal static Task<PagedResponse<CampaignVariantResponse>> VariantsAsync(Browser member, Guid campaignId, string query = "") =>
        member.GetAsync<PagedResponse<CampaignVariantResponse>>($"{Campaigns}/{campaignId}/variants{query}");

    internal static Task<PagedResponse<CampaignResponse>> ListAsync(Browser member, string query = "") =>
        member.GetAsync<PagedResponse<CampaignResponse>>($"{Campaigns}{query}");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
