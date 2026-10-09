using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

/// <summary>
/// What a member does with a Rendered Video once the worker has made it: approve
/// it, download it, find it in the library and delete it. The tests that only read
/// share two Rendered Videos; one that approves or deletes has the worker render its own.
/// </summary>
public sealed class RenderedVideoTests(AffiVideoApp app)
{
    internal const string Videos = "/api/v1/rendered-videos";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Approving_a_Rendered_Video_records_who_and_when_and_is_written_to_the_audit_log()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var editorIs = (await editor.GetAsync<SessionResponse>("/api/v1/session")).Member;
        var video = await RenderedAsync(app, owner);
        var before = DateTimeOffset.UtcNow;

        var response = await ApproveAsync(editor, video.Id);
        var again = await ApproveAsync(owner, video.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await ReadAsync<RenderedVideoResponse>(response);
        Assert.Equal(RenderedVideoState.Approved, approved.State);
        Assert.Equal((editorIs.Id, editorIs.Email), (approved.ApprovedByMemberId, approved.ApprovedByEmail));
        Assert.InRange(approved.ApprovedAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        // Approved once: the second member is told so, and the first stays the one who approved.
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This Rendered Video is already approved.", (await ReadAsync<ProblemDetails>(again)).Detail);
        var read = await owner.GetAsync<RenderedVideoResponse>($"{Videos}/{video.Id}");
        Assert.Equal((RenderedVideoState.Approved, editorIs.Id), (read.State, read.ApprovedByMemberId));
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "rendered-video.approved");
        Assert.Equal((video.Id, editorIs.Id), (entry.SubjectId, entry.ActorMemberId));
        Assert.InRange(entry.OccurredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task A_Rendered_Video_that_is_ready_for_review_says_nobody_has_approved_it()
    {
        var library = await LibraryAsync(app);
        using var member = await app.SignedInAsync(library.Owner);

        var waiting = await member.GetAsync<RenderedVideoResponse>($"{Videos}/{library.Flask.Id}");

        Assert.Equal(RenderedVideoState.ReadyForReview, waiting.State);
        Assert.Null(waiting.ApprovedByMemberId);
        Assert.Null(waiting.ApprovedByEmail);
        Assert.Null(waiting.ApprovedAt);
    }

    [Fact]
    public async Task Download_is_refused_until_the_Rendered_Video_is_approved_and_is_then_the_MP4_that_was_previewed()
    {
        var library = await LibraryAsync(app);
        using var member = await app.SignedInAsync(library.Owner);
        var waiting = $"{Videos}/{library.Flask.Id}";
        var approved = $"{Videos}/{library.Earbuds.Id}";

        var previewOfWaiting = await member.GetAsync($"{waiting}/content");
        var tooSoon = await member.GetAsync($"{waiting}/download");
        var preview = await member.GetAsync($"{approved}/content");
        var download = await member.GetAsync($"{approved}/download");

        // Preview needs no approval.
        Assert.Equal(HttpStatusCode.OK, previewOfWaiting.StatusCode);
        Assert.Equal(library.Flask.SizeInBytes, (await previewOfWaiting.Content.ReadAsByteArrayAsync(Cancellation)).Length);
        Assert.Equal(HttpStatusCode.Conflict, tooSoon.StatusCode);
        Assert.Equal(
            "This Rendered Video has not been approved. Approve it, then download it.",
            (await ReadAsync<ProblemDetails>(tooSoon)).Detail);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("video/mp4", download.Content.Headers.ContentType?.MediaType);
        var previewed = await preview.Content.ReadAsByteArrayAsync(Cancellation);
        Assert.Equal(library.Earbuds.SizeInBytes, previewed.Length);
        Assert.Equal(previewed, await download.Content.ReadAsByteArrayAsync(Cancellation));
        // It is saved as a file, named after the Product, and not played in the page.
        var saved = download.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", saved.DispositionType);
        Assert.StartsWith("Tai nghe AirBeat X1 ", saved.FileNameStar);
        Assert.EndsWith(".mp4", saved.FileNameStar);
    }

    [Fact]
    public async Task When_two_members_approve_the_same_Rendered_Video_at_once_one_is_recorded_and_the_other_refused()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var video = await RenderedAsync(app, owner);
        // The tokens are fetched beforehand, so that the requests themselves leave together.
        var ownerToken = await owner.AntiforgeryTokenAsync();
        var editorToken = await editor.AntiforgeryTokenAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(turn => Task.Run(async () =>
        {
            var (member, token) = turn % 2 == 0 ? (owner, ownerToken) : (editor, editorToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Videos}/{video.Id}/approve");
            request.Headers.Add(Browser.AntiforgeryHeader, token);
            return await member.Http.SendAsync(request, Cancellation);
        })));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Equal(11, answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict));
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "rendered-video.approved");
        Assert.Equal((await owner.GetAsync<RenderedVideoResponse>($"{Videos}/{video.Id}")).ApprovedByMemberId, entry.ActorMemberId);
    }

    [Fact]
    public async Task The_library_lists_every_Rendered_Video_with_what_it_was_made_from_newest_first()
    {
        var library = await LibraryAsync(app);
        using var member = await app.SignedInAsync(library.Owner);

        var listed = await ListAsync(member);

        Assert.Equal([library.Flask.Id, library.Earbuds.Id], listed.Items.Select(video => video.Id));
        Assert.Equal(2, listed.Total);
        var flask = listed.Items[0];
        Assert.Equal("Bình giữ nhiệt Lumo 500", flask.ProductName);
        Assert.Equal("Đặt mua bình trong hôm nay", flask.ProjectObjective);
        Assert.Equal(CreativeTemplate.ProductShowcase, flask.CreativeTemplate);
        Assert.Equal("Bạn vẫn uống nước ấm lạnh ngắt?", flask.Hook);
        Assert.Equal(1, flask.StoryboardVersion);
        Assert.Equal(15_000, flask.DurationMs);
        // Enough to open the Variant it belongs to, and the Product.
        var variant = await member.GetAsync<VariantResponse>($"{VariantTests.Variants(flask.ProjectId)}/{flask.VariantId}");
        Assert.Equal(flask.Hook, variant.Hook);
        Assert.Equal(flask.ProductName, (await member.GetAsync<ProductResponse>($"/api/v1/products/{flask.ProductId}")).Name);
        // The same Rendered Video, whichever way it is asked for.
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(flask, AffiVideoApp.Json),
            System.Text.Json.JsonSerializer.Serialize(await member.GetAsync<RenderedVideoResponse>($"{Videos}/{flask.Id}"), AffiVideoApp.Json));
    }

    [Theory]
    [InlineData("?search=lumo", "flask")]
    [InlineData("?search=%20AIRBEAT%20", "earbuds")]
    // What a Project is called by: its objective.
    [InlineData("?search=trong%20h%C3%B4m%20nay", "flask")]
    [InlineData("?search=xem%20th%C3%AAm", "earbuds")]
    [InlineData("?search=", "flask,earbuds")]
    [InlineData("?search=nothing-is-called-this", "")]
    // What is searched for is text, never a pattern.
    [InlineData("?search=%25", "")]
    [InlineData("?search=_", "")]
    [InlineData("?state=Approved", "earbuds")]
    [InlineData("?state=ReadyForReview", "flask")]
    [InlineData("?creativeTemplate=ProductShowcase", "flask,earbuds")]
    [InlineData("?creativeTemplate=LuxuryCinematic", "")]
    [InlineData("?sort=NewestFirst", "flask,earbuds")]
    [InlineData("?sort=OldestFirst", "earbuds,flask")]
    [InlineData("?sort=OldestFirst&pageSize=1&page=2", "flask")]
    [InlineData("?search=b&state=Approved&creativeTemplate=ProductShowcase&sort=OldestFirst", "earbuds")]
    [InlineData("?search=lumo&state=Approved", "")]
    public async Task The_library_is_searched_by_Product_or_Project_filtered_by_state_and_creative_template_and_sorted_by_date(
        string query, string expected)
    {
        var library = await LibraryAsync(app);
        using var member = await app.SignedInAsync(library.Owner);

        var listed = await ListAsync(member, query);

        var names = new Dictionary<Guid, string> { [library.Flask.Id] = "flask", [library.Earbuds.Id] = "earbuds" };
        Assert.Equal(expected, string.Join(',', listed.Items.Select(video => names[video.Id])));
    }

    [Theory]
    [InlineData("?state=Deleted")]
    [InlineData("?state=7")]
    [InlineData("?creativeTemplate=Slideshow")]
    [InlineData("?creativeTemplate=9")]
    [InlineData("?sort=Random")]
    [InlineData("?sort=5")]
    public async Task The_library_refuses_a_state_creative_template_or_order_that_does_not_exist(string query)
    {
        var library = await LibraryAsync(app);
        using var member = await app.SignedInAsync(library.Owner);

        var response = await member.GetAsync($"{Videos}{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_Rendered_Video_removes_its_file_and_is_written_to_the_audit_log()
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var memberId = (await member.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var video = await RenderedAsync(app, member);
        var stored = $"organizations/{organization.Id}/rendered-videos/";
        Assert.Equal([$"{stored}{video.Id}.mp4"], await app.StoredKeysAsync(stored));
        (await ApproveAsync(member, video.Id)).EnsureSuccessStatusCode();

        var deleted = await member.DeleteAsync($"{Videos}/{video.Id}");
        var again = await member.DeleteAsync($"{Videos}/{video.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty(await app.StoredKeysAsync(stored));
        HttpResponseMessage[] gone =
        [
            await member.GetAsync($"{Videos}/{video.Id}"),
            await member.GetAsync($"{Videos}/{video.Id}/content"),
            await member.GetAsync($"{Videos}/{video.Id}/download"),
            await ApproveAsync(member, video.Id),
        ];
        Assert.All(gone, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(0, (await ListAsync(member)).Total);
        // The job that made it stays as the record of the render, with nothing to show for it.
        var job = await member.GetAsync<RenderJobResponse>($"/api/v1/render-jobs/{video.RenderJobId}");
        Assert.Equal(RenderJobState.Completed, job.State);
        Assert.Null(job.RenderedVideoId);
        var log = await member.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "rendered-video.deleted");
        Assert.Equal((video.Id, memberId), (entry.SubjectId, entry.ActorMemberId));

        // With its last Rendered Video gone, nothing holds the Project any longer.
        var project = await member.DeleteAsync($"{ProjectTests.Projects}/{video.ProjectId}");
        Assert.Equal(HttpStatusCode.NoContent, project.StatusCode);
    }

    [Fact]
    public async Task Approval_download_and_delete_are_refused_to_someone_who_is_not_signed_in()
    {
        var library = await LibraryAsync(app);
        using var stranger = app.NewBrowser();

        HttpResponseMessage[] responses =
        [
            await stranger.GetAsync(Videos),
            await ApproveAsync(stranger, library.Flask.Id),
            await stranger.GetAsync($"{Videos}/{library.Earbuds.Id}/download"),
            await stranger.DeleteAsync($"{Videos}/{library.Earbuds.Id}"),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    internal static Task<HttpResponseMessage> ApproveAsync(Browser member, Guid videoId) =>
        member.PostAsync($"{Videos}/{videoId}/approve", new { });

    internal static Task<PagedResponse<RenderedVideoResponse>> ListAsync(Browser member, string query = "") =>
        member.GetAsync<PagedResponse<RenderedVideoResponse>>($"{Videos}{query}");

    /// <summary>
    /// A 15-second Rendered Video of a new Product of the member's Organization, ready
    /// for review. The Product's one photo came already cut out, so no model runs.
    /// </summary>
    internal static async Task<RenderedVideoResponse> RenderedAsync(
        AffiVideoApp app, Browser member, string product = "Lumo 500",
        string objective = "Bấm vào liên kết", string hook = "Bạn vẫn dùng bình nhựa?")
    {
        await app.WorkerAsync();
        var productId = await StoryboardTests.NewProductAsync(member, product);
        await ProductAssetTests.UploadedAsync(member, productId, ProductAssetKind.Photo, RenderTests.Bottle(onBackdrop: false));
        await StoryboardTests.ConfirmedFactAsync(member, productId, "Giữ lạnh suốt 24 giờ");
        var project = await ProjectTests.CreateAsync(
            member, ProjectTests.Valid(productId) with { TargetDurationSeconds = 15, Objective = objective });
        var variant = await VariantTests.AddAsync(member, project.Id, new VariantRequest(CreativeTemplate.ProductShowcase, hook));
        var storyboard = await StoryboardTests.GeneratedAsync(member, variant);

        var job = await RenderTests.EndedAsync(member, await RenderTests.SubmittedAsync(member, variant, storyboard.Version));

        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await app.WorkerLogAsync()}");
        return await member.GetAsync<RenderedVideoResponse>($"{Videos}/{job.RenderedVideoId}");
    }

    /// <summary>
    /// An Organization with two Rendered Videos, for every test that only reads the
    /// library: one of earbuds, rendered first and approved, and one of a flask,
    /// rendered after it and still ready for review.
    /// </summary>
    internal static Task<Library> LibraryAsync(AffiVideoApp app) => app.OnceAsync(async () =>
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var earbuds = await RenderedAsync(app, member, "Tai nghe AirBeat X1", "Bấm xem thêm", "Bạn còn nghe nhạc bằng tai nghe dây?");
        var flask = await RenderedAsync(app, member, "Bình giữ nhiệt Lumo 500", "Đặt mua bình trong hôm nay", "Bạn vẫn uống nước ấm lạnh ngắt?");
        (await ApproveAsync(member, earbuds.Id)).EnsureSuccessStatusCode();
        return new Library(organization.Owner, organization.Id, earbuds, flask);
    });

    internal sealed record Library(Credentials Owner, Guid OrganizationId, RenderedVideoResponse Earbuds, RenderedVideoResponse Flask);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
