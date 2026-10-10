using System.Net;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The render jobs of an Organization that have not ended, as the dashboard asks for
/// them: what is queued or being rendered now, with what each is a video of.
/// </summary>
public sealed class RenderJobsInProgressTests(AffiVideoApp app)
{
    private const string InProgress = "/api/v1/render-jobs/in-progress";

    [Fact]
    public async Task A_member_sees_the_jobs_of_their_Organization_that_have_not_ended_newest_first_with_what_each_renders()
    {
        // A queue no worker serves: what is queued stays queued.
        await using var stack = await app.NewStackAsync();
        using var member = await stack.NewMemberAsync();
        using var memberOfAnother = await stack.NewMemberAsync();
        using var stranger = stack.NewBrowser();
        var product = await StoryboardTests.NewProductAsync(member, "Lumo 500");
        await StoryboardTests.UploadPhotoAsync(member, product);
        await StoryboardTests.ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var first = await StoryboardTests.NewVariantAsync(member, product, hook: "Nghe nhạc cả ngày");
        var second = await StoryboardTests.NewVariantAsync(
            member, product, creativeTemplate: CreativeTemplate.LuxuryCinematic, hook: "Âm thanh sang trọng");
        await StoryboardTests.GeneratedAsync(member, first);
        await StoryboardTests.GeneratedAsync(member, second);
        var again = await StoryboardTests.GeneratedAsync(member, second);

        var none = await member.GetAsync<PagedResponse<RenderJobInProgressResponse>>(InProgress);
        var older = await RenderTests.SubmittedAsync(member, first, 1);
        var newer = await RenderTests.SubmittedAsync(member, second, again.Version);
        var both = await member.GetAsync<PagedResponse<RenderJobInProgressResponse>>(InProgress);
        var onePerPage = await member.GetAsync<PagedResponse<RenderJobInProgressResponse>>($"{InProgress}?page=2&pageSize=1");

        Assert.Equal(0, none.Total);
        Assert.Equal(2, both.Total);
        Assert.Equal([newer.Id, older.Id], both.Items.Select(item => item.Job.Id));
        var listed = both.Items[0];
        Assert.Equal(RenderJobState.Queued, listed.Job.State);
        Assert.Equal(
            (product, "Lumo 500", second.ProjectId, second.Id, CreativeTemplate.LuxuryCinematic, "Âm thanh sang trọng", 2),
            (listed.ProductId, listed.ProductName, listed.ProjectId, listed.VariantId, listed.CreativeTemplate, listed.Hook,
                listed.StoryboardVersion));
        Assert.Equal((2, 2, 1), (onePerPage.Total, onePerPage.Page, onePerPage.PageSize));
        Assert.Equal([older.Id], onePerPage.Items.Select(item => item.Job.Id));

        // Another Organization's member sees none of them, and nobody sees any without signing in.
        Assert.Equal(0, (await memberOfAnother.GetAsync<PagedResponse<RenderJobInProgressResponse>>(InProgress)).Total);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync(InProgress)).StatusCode);

        // A job that has ended is no longer in progress.
        (await member.PostAsync($"/api/v1/render-jobs/{newer.Id}/cancel", new { })).EnsureSuccessStatusCode();
        var left = await member.GetAsync<PagedResponse<RenderJobInProgressResponse>>(InProgress);
        Assert.Equal([older.Id], left.Items.Select(item => item.Job.Id));
        Assert.Equal(1, left.Total);
    }

    [Fact]
    public async Task A_job_that_has_made_its_Rendered_Video_is_no_longer_in_progress()
    {
        var rendered = await RenderTests.RenderedAsync(app);
        using var member = await app.SignedInAsync(rendered.Owner);

        var listed = await member.GetAsync<PagedResponse<RenderJobInProgressResponse>>($"{InProgress}?pageSize=200");

        Assert.DoesNotContain(listed.Items, item => item.Job.Id == rendered.Job.Id);
    }
}
