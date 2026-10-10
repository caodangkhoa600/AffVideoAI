using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

public sealed class LabDashboardTests(AffiVideoApp app)
{
    [Fact]
    public async Task Dashboard_uses_latest_snapshots_and_keeps_missing_counts_unknown()
    {
        var lab = await app.CreateOrganizationAsync(affiliateLab: true);
        using var member = await app.SignedInAsync(lab.Owner);
        var video = await RenderedVideoTests.RenderedAsync(app, member);
        (await RenderedVideoTests.ApproveAsync(member, video.Id)).EnsureSuccessStatusCode();
        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        var first = await PublishedPostTests.RecordedAsync(member, video.Id, account.Id, new DateOnly(2026, 10, 1));
        var second = await PublishedPostTests.RecordedAsync(member, video.Id, account.Id, new DateOnly(2026, 10, 2));
        var at = new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
        await PerformanceSnapshotTests.RecordedAsync(member, first.Id, new PerformanceSnapshotRequest(at, 100, 10, 2, 3, 5));
        await PerformanceSnapshotTests.RecordedAsync(member, first.Id, new PerformanceSnapshotRequest(at.AddDays(1), 200, 20, 4, 6, 10));
        await PerformanceSnapshotTests.RecordedAsync(member, second.Id, new PerformanceSnapshotRequest(at, 50, Clicks: null));

        var dashboard = await member.GetAsync<LabDashboardResponse>("/api/v1/lab/dashboard");
        var product = Assert.Single(dashboard.Products, g => g.Key == video.ProductId.ToString());
        Assert.Equal(2, product.Posts);
        Assert.Equal(250, product.Views.Value);
        Assert.Null(product.Likes.Value);
        Assert.Null(product.Clicks.Value);
        Assert.Null(product.ClickThroughRate);
        Assert.Null(product.ConversionRate);
        Assert.NotEmpty(product.Views.Sources);

        await PerformanceSnapshotTests.RecordedAsync(member, second.Id,
            new PerformanceSnapshotRequest(at.AddDays(1), 50, 1, 0, 0, 5));
        await CommissionRecordTests.RecordedAsync(member,
            CommissionRecordTests.Valid(productId: video.ProductId, currency: "QDZ", orders: 3, commission: 90));
        var campaignResponse = await member.PostAsync("/api/v1/lab/campaigns", new CampaignRequest($"Test {Guid.NewGuid():N}"));
        campaignResponse.EnsureSuccessStatusCode();
        var campaign = (await campaignResponse.Content.ReadFromJsonAsync<CampaignResponse>(
            AffiVideoApp.Json, TestContext.Current.CancellationToken))!;
        (await member.PutAsync($"/api/v1/lab/campaigns/{campaign.Id}/variants/{video.VariantId}", new { })).EnsureSuccessStatusCode();

        dashboard = await member.GetAsync<LabDashboardResponse>("/api/v1/lab/dashboard");
        product = Assert.Single(dashboard.Products, g => g.Key == video.ProductId.ToString());
        Assert.Equal(15, product.Clicks.Value);
        Assert.Equal(0.06m, product.ClickThroughRate);
        Assert.Equal(0.2m, product.ConversionRate);
        Assert.Equal(90m, Assert.Single(product.Commission, c => c.Currency == "QDZ").Net);
        Assert.NotEmpty(product.ClickThroughRateSources);
        Assert.NotEmpty(product.ConversionRateSources);
        var templateGroup = Assert.Single(dashboard.CreativeTemplates, g => g.Key == video.CreativeTemplate.ToString());
        var hookGroup = Assert.Single(dashboard.Hooks, g => g.Key == video.Hook);
        Assert.Equal(250, templateGroup.Views.Value);
        Assert.Equal(250, hookGroup.Views.Value);
        Assert.Equal(15, templateGroup.Clicks.Value);
        Assert.Equal(15, hookGroup.Clicks.Value);
        var campaignGroup = Assert.Single(dashboard.Campaigns, g => g.Key == campaign.Id.ToString());
        Assert.Equal(250, campaignGroup.Views.Value);
        Assert.Equal(90m, Assert.Single(campaignGroup.Commission, c => c.Currency == "QDZ").Net);
        var anotherLab = await app.CreateOrganizationAsync(affiliateLab: true);
        using var anotherMember = await app.SignedInAsync(anotherLab.Owner);
        var anotherDashboard = await anotherMember.GetAsync<LabDashboardResponse>("/api/v1/lab/dashboard");
        Assert.DoesNotContain(anotherDashboard.Products, g => g.Key == video.ProductId.ToString());
        Assert.DoesNotContain(anotherDashboard.Campaigns, g => g.Key == campaign.Id.ToString());
        var emptyCampaignResponse = await member.PostAsync("/api/v1/lab/campaigns", new CampaignRequest($"No posts {Guid.NewGuid():N}"));
        emptyCampaignResponse.EnsureSuccessStatusCode();
        var emptyCampaign = (await emptyCampaignResponse.Content.ReadFromJsonAsync<CampaignResponse>(
            AffiVideoApp.Json, TestContext.Current.CancellationToken))!;
        dashboard = await member.GetAsync<LabDashboardResponse>("/api/v1/lab/dashboard");
        var small = Assert.Single(dashboard.Campaigns, g => g.Key == emptyCampaign.Id.ToString());
        Assert.True(small.TooSmallToCompare);
        Assert.Null(small.Views.Value);
    }

    [Fact]
    public async Task Dashboard_is_empty_for_a_new_Lab_and_hidden_from_other_Organizations()
    {
        var lab = await app.CreateOrganizationAsync(affiliateLab: true);
        using var member = await app.SignedInAsync(lab.Owner);
        var dashboard = await member.GetAsync<LabDashboardResponse>("/api/v1/lab/dashboard");
        Assert.Empty(dashboard.Products);
        Assert.Empty(dashboard.CreativeTemplates);
        Assert.Empty(dashboard.Hooks);
        Assert.Empty(dashboard.Campaigns);

        var ordinary = await app.CreateOrganizationAsync();
        using var visitor = await app.SignedInAsync(ordinary.Owner);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/api/v1/lab/dashboard")).StatusCode);
    }
}
