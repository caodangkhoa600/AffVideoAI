using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The Performance Snapshots of a Published Post. The tests share the Lab
/// Organization of <see cref="PublishedPostTests"/>; each records a Published
/// Post of its own, so its snapshots are exactly the ones it entered.
/// </summary>
public sealed class PerformanceSnapshotTests(AffiVideoApp app)
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static string Snapshots(Guid postId) => $"{PublishedPostTests.Posts}/{postId}/performance-snapshots";

    [Fact]
    public async Task A_Performance_Snapshot_is_recorded_as_manual_entry_and_read_back()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;
        var before = DateTimeOffset.UtcNow;

        var response = await member.PostAsync(
            Snapshots(post.Id), new PerformanceSnapshotRequest(Monday, Views: 1200, Likes: 85, Comments: 4, Shares: 9, Clicks: 31));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var snapshot = await ReadAsync<PerformanceSnapshotResponse>(response);
        Assert.Equal(post.Id, snapshot.PublishedPostId);
        Assert.Equal(Monday, snapshot.TakenAt);
        Assert.Equal(
            (1200L, 85L, 4L, 9L, 31L),
            (snapshot.Views!.Value, snapshot.Likes!.Value, snapshot.Comments!.Value, snapshot.Shares!.Value, snapshot.Clicks!.Value));
        Assert.Equal(PerformanceSource.Manual, snapshot.Source);
        Assert.InRange(snapshot.RecordedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Empty(snapshot.LowerThanPrevious);
        var listed = await ListAsync(member, post.Id);
        Assert.Equal(1, listed.Total);
        Assert.Equal(snapshot.Id, Assert.Single(listed.Items).Id);
    }

    [Fact]
    public async Task A_moment_given_in_local_time_is_the_same_moment()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;

        var response = await AffiliateLabTests.SendJsonAsync(
            member, HttpMethod.Post, Snapshots(post.Id), """{"takenAt":"2026-10-05T16:00:00+07:00","views":10}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Monday, (await ReadAsync<PerformanceSnapshotResponse>(response)).TakenAt);
    }

    [Fact]
    public async Task A_metric_left_empty_is_unknown_not_zero()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;

        var response = await AffiliateLabTests.SendJsonAsync(
            member, HttpMethod.Post, Snapshots(post.Id), """{"takenAt":"2026-10-05T09:00:00Z","views":500,"clicks":0}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var listed = Assert.Single((await ListAsync(member, post.Id)).Items);
        Assert.Equal((500L, 0L), (listed.Views!.Value, listed.Clicks!.Value));
        Assert.Null(listed.Likes);
        Assert.Null(listed.Comments);
        Assert.Null(listed.Shares);
        // Unknown is said as unknown, not left for a reader to take as zero.
        var raw = await (await member.GetAsync(Snapshots(post.Id))).Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("\"likes\":null", raw);
        Assert.Contains("\"clicks\":0", raw);
    }

    [Fact]
    public async Task The_current_figure_of_a_Published_Post_is_its_latest_snapshot()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;
        Assert.Null(post.CurrentPerformance);

        var tuesday = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(1), Views: 900, Likes: 40));
        // Entered afterwards, but about an earlier moment: it is history, not the current figure.
        var monday = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday, Views: 300));

        var current = (await member.GetAsync<PublishedPostResponse>($"{PublishedPostTests.Posts}/{post.Id}")).CurrentPerformance;
        Assert.NotNull(current);
        Assert.Equal(tuesday.Id, current.SnapshotId);
        Assert.Equal((Monday.AddDays(1), 900L, 40L), (current.TakenAt, current.Views!.Value, current.Likes!.Value));
        Assert.Null(current.Clicks);
        Assert.Equal((PerformanceSource.Manual, tuesday.RecordedAt), (current.Source, current.RecordedAt));
        // The list of Published Posts carries the same figure.
        var inList = Assert.Single(
            (await PublishedPostTests.ListAsync(member, $"?socialAccountId={post.SocialAccount.Id}")).Items, p => p.Id == post.Id);
        Assert.Equal(current, inList.CurrentPerformance);
        Assert.Equal([tuesday.Id, monday.Id], (await ListAsync(member, post.Id)).Items.Select(s => s.Id));
    }

    [Fact]
    public async Task A_wrong_figure_is_corrected_by_a_newer_snapshot_and_the_wrong_one_is_kept()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;
        var wrong = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday, Views: 12000));

        // About the very same moment: the one entered later is the newer.
        var right = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday, Views: 1200));

        var listed = await ListAsync(member, post.Id);
        Assert.Equal([(right.Id, 1200L), (wrong.Id, 12000L)], listed.Items.Select(s => (s.Id, s.Views!.Value)));
        // A correction is not compared with what it corrects, so it carries no warning.
        Assert.Empty(right.LowerThanPrevious);
        Assert.All(listed.Items, snapshot => Assert.Empty(snapshot.LowerThanPrevious));
        Assert.Equal(right.Id, (await member.GetAsync<PublishedPostResponse>($"{PublishedPostTests.Posts}/{post.Id}")).CurrentPerformance!.SnapshotId);
        // Nothing changes or removes a snapshot.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await member.PutAsync(Snapshots(post.Id), new PerformanceSnapshotRequest(Monday, Views: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PutAsync($"{Snapshots(post.Id)}/{wrong.Id}", new PerformanceSnapshotRequest(Monday, Views: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.DeleteAsync($"{Snapshots(post.Id)}/{wrong.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_snapshot_with_totals_lower_than_the_previous_one_is_accepted_and_says_which()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;
        await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday, Views: 1000, Likes: 50, Shares: 5));

        var lower = await RecordedAsync(
            member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(1), Views: 800, Likes: 50, Comments: 2, Clicks: 1));
        var higher = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(2), Views: 900, Likes: 49));

        // Only what both snapshots know can be lower: comments and clicks were unknown before, shares are unknown now.
        Assert.Equal([PerformanceMetric.Views], lower.LowerThanPrevious);
        Assert.Equal([PerformanceMetric.Likes], higher.LowerThanPrevious);
        var listed = await ListAsync(member, post.Id);
        Assert.Equal(
            [[PerformanceMetric.Likes], [PerformanceMetric.Views], []],
            listed.Items.Select(s => s.LowerThanPrevious.ToArray()));
        // The previous one is the one before it in time, on whichever page that falls.
        var secondPage = await ListAsync(member, post.Id, "?page=2&pageSize=1");
        Assert.Equal([PerformanceMetric.Views], Assert.Single(secondPage.Items).LowerThanPrevious);
        Assert.Equal(3, secondPage.Total);
        // Two snapshots about one moment are both compared with the one before that moment.
        var again = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(2), Views: 700));
        Assert.Equal([PerformanceMetric.Views], again.LowerThanPrevious);
        Assert.Equal(
            [[PerformanceMetric.Views], [PerformanceMetric.Likes]],
            (await ListAsync(member, post.Id, "?pageSize=2")).Items.Select(s => s.LowerThanPrevious.ToArray()));
    }

    [Fact]
    public async Task A_snapshot_entered_about_an_earlier_moment_is_compared_with_the_one_before_that_moment()
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;
        await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday, Views: 100));
        await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(2), Views: 300));

        var between = await RecordedAsync(member, post.Id, new PerformanceSnapshotRequest(Monday.AddDays(1), Views: 400));

        // 400 is not lower than Monday's 100. Wednesday's 300 is now lower than the one before it.
        Assert.Empty(between.LowerThanPrevious);
        Assert.Equal(
            [(300L, 1), (400L, 0), (100L, 0)],
            (await ListAsync(member, post.Id)).Items.Select(s => (s.Views!.Value, s.LowerThanPrevious.Count)));
    }

    [Theory]
    [InlineData("""{"takenAt":"2026-10-05T09:00:00Z"}""", "views")]
    [InlineData("""{"takenAt":"2026-10-05T09:00:00Z","views":-1}""", "views")]
    [InlineData("""{"takenAt":"2026-10-05T09:00:00Z","views":10,"shares":-3}""", "shares")]
    [InlineData("""{"takenAt":"2999-01-01T00:00:00Z","views":10}""", "takenAt")]
    public async Task A_snapshot_needs_a_figure_none_below_zero_and_a_moment_that_has_passed(string body, string refusedField)
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;

        var response = await AffiliateLabTests.SendJsonAsync(member, HttpMethod.Post, Snapshots(post.Id), body);

        Assert.Equal([refusedField], await PublishedPostTests.RefusedFieldsAsync(response));
        Assert.Equal(0, (await ListAsync(member, post.Id)).Total);
    }

    [Theory]
    [InlineData("""{"views":10}""")]
    [InlineData("""{"takenAt":"yesterday","views":10}""")]
    [InlineData("""{"takenAt":"2026-10-05T09:00:00Z","views":1.5}""")]
    [InlineData("""{"takenAt":"2026-10-05T09:00:00Z","views":"many"}""")]
    public async Task A_snapshot_that_cannot_be_read_is_refused(string body)
    {
        var (member, post) = await PublishedPostAsync();
        using var _ = member;

        var response = await AffiliateLabTests.SendJsonAsync(member, HttpMethod.Post, Snapshots(post.Id), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await ListAsync(member, post.Id)).Total);
    }

    [Fact]
    public async Task A_snapshot_of_a_Published_Post_there_is_not_is_not_found()
    {
        var lab = await PublishedPostTests.LabAsync(app);
        using var member = await app.SignedInAsync(lab.Owner);
        var nothing = Guid.NewGuid();

        var recorded = await member.PostAsync(Snapshots(nothing), new PerformanceSnapshotRequest(Monday, Views: 10));
        var listed = await member.GetAsync(Snapshots(nothing));

        Assert.Equal(HttpStatusCode.NotFound, recorded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, listed.StatusCode);
    }

    // A Published Post of this test's own, on an account of its own, in the Lab Organization the tests share.
    private async Task<(Browser Member, PublishedPostResponse Post)> PublishedPostAsync()
    {
        var lab = await PublishedPostTests.LabAsync(app);
        var member = await app.SignedInAsync(lab.Owner);
        return (member, await PublishedPostAsync(member, lab.Earbuds.Id));
    }

    internal static async Task<PublishedPostResponse> PublishedPostAsync(Browser member, Guid renderedVideoId)
    {
        var account = await PublishedPostTests.AccountAsync(member, SocialPlatform.TikTok, PublishedPostTests.NewHandle());
        return await PublishedPostTests.RecordedAsync(member, renderedVideoId, account.Id, new DateOnly(2026, 10, 4));
    }

    internal static async Task<PerformanceSnapshotResponse> RecordedAsync(Browser member, Guid postId, PerformanceSnapshotRequest request)
    {
        var response = await member.PostAsync(Snapshots(postId), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PerformanceSnapshotResponse>(response);
    }

    internal static Task<PagedResponse<PerformanceSnapshotResponse>> ListAsync(Browser member, Guid postId, string query = "") =>
        member.GetAsync<PagedResponse<PerformanceSnapshotResponse>>($"{Snapshots(postId)}{query}");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
