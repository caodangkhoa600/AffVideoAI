using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

/// <summary>
/// What withdrawing a Fact does to the work that used it: every Storyboard version
/// and Rendered Video is Flagged for Review, kept, and cleared by a member.
/// </summary>
public sealed class FlaggedForReviewTests(AffiVideoApp app)
{
    private const string Battery = "Pin dùng liên tục 30 giờ";
    private const string Noise = "Chống ồn chủ động";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Withdrawing_a_Fact_flags_every_Storyboard_version_that_used_it_with_the_Fact_and_the_text_it_used()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await StoryboardTests.NewProductAsync(member);
        await StoryboardTests.UploadPhotoAsync(member, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(member, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(member, product);
        var first = await StoryboardTests.GeneratedAsync(member, variant);
        var noise = await StoryboardTests.ConfirmedFactAsync(member, product, Noise);
        var second = await StoryboardTests.GeneratedAsync(member, variant);
        // A person's own words in place of the Facts: this version no longer rests on either.
        await StoryboardEditTests.EditedAsync(
            member, variant, 2, StoryboardEditTests.Changing(second, new(3, OnScreenText: ["Nghe nhạc cả ngày"])));
        Assert.Empty(first.Flags);
        Assert.Empty(second.Flags);
        var before = DateTimeOffset.UtcNow;

        (await FactTests.WithdrawAsync(member, product, battery.Id)).EnsureSuccessStatusCode();

        var versions = (await StoryboardTests.ListAsync(member, variant)).Items.ToDictionary(storyboard => storyboard.Version);
        var flag = Assert.Single(versions[1].Flags);
        Assert.Equal((battery.Id, Battery), (flag.FactId, flag.Text));
        Assert.InRange(flag.WithdrawnAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal([battery.Id], versions[2].Flags.Select(raised => raised.FactId));
        Assert.Empty(versions[3].Flags);
        // The same answer for one version as in the list, and nothing of the version itself has changed.
        var read = await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1");
        Assert.Equal([flag], read.Flags);
        Assert.Equal(first.Scenes.Select(scene => scene.OnScreenText), read.Scenes.Select(scene => scene.OnScreenText));
        Assert.Equal(first.Scenes[2].Facts, read.Scenes[2].Facts);
        // A Storyboard generated now rests on what is still Confirmed, and is not flagged.
        var fourth = await StoryboardTests.GeneratedAsync(member, variant);
        Assert.Equal([new SceneFactResponse(noise.Id, Noise)], fourth.Scenes[2].Facts);
        Assert.Empty(fourth.Flags);

        // Changing a Fact withdraws it too, and a second Withdrawn Fact is a second flag on the version that used both.
        (await FactTests.ReplaceAsync(member, product, noise.Id, new FactRequest("Chống ồn chủ động 40 dB", "vi", null))).EnsureSuccessStatusCode();

        versions = (await StoryboardTests.ListAsync(member, variant)).Items.ToDictionary(storyboard => storyboard.Version);
        Assert.Equal([battery.Id], versions[1].Flags.Select(raised => raised.FactId));
        Assert.Equal([(battery.Id, Battery), (noise.Id, Noise)], versions[2].Flags.Select(raised => (raised.FactId, raised.Text)));
        Assert.Empty(versions[3].Flags);
    }

    [Fact]
    public async Task A_member_clears_the_flag_on_a_Storyboard_version_and_the_audit_log_records_who_and_when()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var editorId = (await editor.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var product = await StoryboardTests.NewProductAsync(owner);
        await StoryboardTests.UploadPhotoAsync(owner, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(owner, product, Battery);
        var noise = await StoryboardTests.ConfirmedFactAsync(owner, product, Noise);
        var variant = await StoryboardTests.NewVariantAsync(owner, product);
        await StoryboardTests.GeneratedAsync(owner, variant);
        await StoryboardTests.GeneratedAsync(owner, variant);
        var notFlagged = await ClearStoryboardAsync(editor, variant, 1, battery.Id);
        (await FactTests.WithdrawAsync(owner, product, battery.Id)).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;

        var response = await ClearStoryboardAsync(editor, variant, 1, battery.Id);
        var again = await ClearStoryboardAsync(owner, variant, 1, battery.Id);

        Assert.Equal(HttpStatusCode.Conflict, notFlagged.StatusCode);
        Assert.Equal("This Storyboard version is not Flagged for Review.", (await ReadAsync<ProblemDetails>(notFlagged)).Detail);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = await ReadAsync<StoryboardResponse>(response);
        Assert.Equal(1, cleared.Version);
        Assert.Empty(cleared.Flags);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        // Each version is reviewed for itself: the other still carries its flag.
        var versions = (await StoryboardTests.ListAsync(owner, variant)).Items.ToDictionary(storyboard => storyboard.Version);
        Assert.Empty(versions[1].Flags);
        Assert.Equal([battery.Id], versions[2].Flags.Select(flag => flag.FactId));
        // The version keeps what it said, and the Fact stays Withdrawn.
        Assert.Equal([Battery, Noise], versions[1].Scenes[2].OnScreenText);
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "storyboard.flag-cleared");
        Assert.Equal((cleared.Id, editorId), (entry.SubjectId, entry.ActorMemberId));
        Assert.InRange(entry.OccurredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));

        // A Fact withdrawn after the review flags the version again, for that Fact alone.
        (await FactTests.WithdrawAsync(owner, product, noise.Id)).EnsureSuccessStatusCode();

        var flaggedAgain = await owner.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1");
        Assert.Equal([(noise.Id, Noise)], flaggedAgain.Flags.Select(flag => (flag.FactId, flag.Text)));

        // Only the flags the member names are cleared: one they have not seen stays, and naming none that is there clears nothing.
        var stale = await ClearStoryboardAsync(owner, variant, 1, battery.Id);
        var unnamed = await ClearStoryboardAsync(owner, variant, 2);
        var one = await ClearStoryboardAsync(owner, variant, 2, battery.Id);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.StartsWith("The flags here have changed since you looked.", (await ReadAsync<ProblemDetails>(stale)).Detail);
        Assert.Equal(HttpStatusCode.BadRequest, unnamed.StatusCode);
        Assert.Equal([noise.Id], (await ReadAsync<StoryboardResponse>(one)).Flags.Select(flag => flag.FactId));
        Assert.Single((await owner.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1")).Flags);

        // A Project is deleted with the record of what was reviewed in it.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{ProjectTests.Projects}/{variant.ProjectId}")).StatusCode);
    }

    [Fact]
    public async Task When_two_members_clear_the_same_flag_at_once_one_clearing_is_recorded_and_the_others_refused()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var product = await StoryboardTests.NewProductAsync(owner);
        await StoryboardTests.UploadPhotoAsync(owner, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(owner, product, Battery);
        var variant = await StoryboardTests.NewVariantAsync(owner, product);
        await StoryboardTests.GeneratedAsync(owner, variant);
        (await FactTests.WithdrawAsync(owner, product, battery.Id)).EnsureSuccessStatusCode();
        // The tokens are fetched beforehand, so that the requests themselves leave together.
        var ownerToken = await owner.AntiforgeryTokenAsync();
        var editorToken = await editor.AntiforgeryTokenAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(turn => Task.Run(async () =>
        {
            var (member, token) = turn % 2 == 0 ? (owner, ownerToken) : (editor, editorToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{StoryboardTests.Storyboards(variant)}/1/clear-flag");
            request.Headers.Add(Browser.AntiforgeryHeader, token);
            request.Content = JsonContent.Create(new ClearFlagRequest([battery.Id]));
            return await member.Http.SendAsync(request, Cancellation);
        })));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Equal(11, answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict));
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        Assert.Single(log.Items, e => e.Action == "storyboard.flag-cleared");
    }

    [Fact]
    public async Task Withdrawing_a_Fact_flags_the_Rendered_Videos_that_used_it_which_stay_viewable_and_downloadable_and_the_library_filters_to_them()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var flask = await RenderedVideoTests.RenderedAsync(app, member);
        var earbuds = await RenderedVideoTests.RenderedAsync(app, member, "Tai nghe AirBeat X1");
        (await RenderedVideoTests.ApproveAsync(member, flask.Id)).EnsureSuccessStatusCode();
        Assert.Empty(flask.Flags);
        Assert.Equal(0, (await RenderedVideoTests.ListAsync(member, "?flagged=true")).Total);
        var fact = Assert.Single((await FactTests.ListAsync(member, flask.ProductId)).Items);

        (await FactTests.WithdrawAsync(member, flask.ProductId, fact.Id)).EnsureSuccessStatusCode();

        var read = await member.GetAsync<RenderedVideoResponse>($"{RenderedVideoTests.Videos}/{flask.Id}");
        var flag = Assert.Single(read.Flags);
        Assert.Equal((fact.Id, "Giữ lạnh suốt 24 giờ"), (flag.FactId, flag.Text));
        // Flagged work is kept as it was: still approved, still played and still downloaded.
        Assert.Equal(RenderedVideoState.Approved, read.State);
        var preview = await member.GetAsync($"{RenderedVideoTests.Videos}/{flask.Id}/content");
        var download = await member.GetAsync($"{RenderedVideoTests.Videos}/{flask.Id}/download");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(flask.SizeInBytes, (await download.Content.ReadAsByteArrayAsync(Cancellation)).Length);
        // The Storyboard version it was rendered from carries the same flag.
        var variant = await member.GetAsync<VariantResponse>($"{VariantTests.Variants(flask.ProjectId)}/{flask.VariantId}");
        Assert.Equal([flag], (await member.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1")).Flags);

        var flagged = await RenderedVideoTests.ListAsync(member, "?flagged=true");
        Assert.Equal([flask.Id], flagged.Items.Select(video => video.Id));
        Assert.Equal(1, flagged.Total);
        Assert.Equal([flag], flagged.Items[0].Flags);
        Assert.Equal([earbuds.Id], (await RenderedVideoTests.ListAsync(member, "?flagged=false")).Items.Select(video => video.Id));
        var all = await RenderedVideoTests.ListAsync(member);
        Assert.Equal([earbuds.Id, flask.Id], all.Items.Select(video => video.Id));
        Assert.Equal([0, 1], all.Items.Select(video => video.Flags.Count));
        Assert.Equal(1, (await RenderedVideoTests.ListAsync(member, "?flagged=true&state=Approved&search=lumo")).Total);
        Assert.Equal(0, (await RenderedVideoTests.ListAsync(member, "?flagged=true&state=ReadyForReview")).Total);
    }

    [Fact]
    public async Task A_member_clears_the_flag_on_a_Rendered_Video_and_the_audit_log_records_who_and_when()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        using var outsider = await SignedInToNewOrganizationAsync();
        var editorId = (await editor.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var video = await RenderedVideoTests.RenderedAsync(app, owner);
        var fact = Assert.Single((await FactTests.ListAsync(owner, video.ProductId)).Items);
        var notFlagged = await ClearVideoAsync(editor, video.Id, fact.Id);
        (await FactTests.WithdrawAsync(owner, video.ProductId, fact.Id)).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;

        var notTheirs = await ClearVideoAsync(outsider, video.Id, fact.Id);
        var response = await ClearVideoAsync(editor, video.Id, fact.Id);
        var again = await ClearVideoAsync(owner, video.Id, fact.Id);

        Assert.Equal(HttpStatusCode.Conflict, notFlagged.StatusCode);
        Assert.Equal("This Rendered Video is not Flagged for Review.", (await ReadAsync<ProblemDetails>(notFlagged)).Detail);
        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = await ReadAsync<RenderedVideoResponse>(response);
        Assert.Equal(video.Id, cleared.Id);
        Assert.Empty(cleared.Flags);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Empty((await owner.GetAsync<RenderedVideoResponse>($"{RenderedVideoTests.Videos}/{video.Id}")).Flags);
        Assert.Equal(0, (await RenderedVideoTests.ListAsync(owner, "?flagged=true")).Total);
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "rendered-video.flag-cleared");
        Assert.Equal((video.Id, editorId), (entry.SubjectId, entry.ActorMemberId));
        Assert.InRange(entry.OccurredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        // The video was reviewed, not the Storyboard version: that one is still flagged.
        var variant = await owner.GetAsync<VariantResponse>($"{VariantTests.Variants(video.ProjectId)}/{video.VariantId}");
        Assert.Single((await owner.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1")).Flags);

        // A video rendered from that version now shows the Withdrawn Fact as well, and says so.
        var job = await RenderTests.EndedAsync(owner, await RenderTests.SubmittedAsync(owner, variant, 1));
        Assert.True(job.State == RenderJobState.Completed, $"The render ended {job.State}: {job.Failure?.Message}\n{await app.WorkerLogAsync()}");
        var later = await owner.GetAsync<RenderedVideoResponse>($"{RenderedVideoTests.Videos}/{job.RenderedVideoId}");
        Assert.Equal([fact.Id], later.Flags.Select(flag => flag.FactId));
        Assert.Equal([later.Id], (await RenderedVideoTests.ListAsync(owner, "?flagged=true")).Items.Select(listed => listed.Id));

        // A Rendered Video is deleted with the record of its review.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{RenderedVideoTests.Videos}/{video.Id}")).StatusCode);
    }

    /// <param name="factIds">The Withdrawn Facts whose flags the member has reviewed.</param>
    internal static Task<HttpResponseMessage> ClearVideoAsync(Browser member, Guid videoId, params Guid[] factIds) =>
        member.PostAsync($"{RenderedVideoTests.Videos}/{videoId}/clear-flag", new ClearFlagRequest(factIds));

    /// <param name="factIds">The Withdrawn Facts whose flags the member has reviewed.</param>
    internal static Task<HttpResponseMessage> ClearStoryboardAsync(Browser member, VariantResponse variant, int version, params Guid[] factIds) =>
        member.PostAsync($"{StoryboardTests.Storyboards(variant)}/{version}/clear-flag", new ClearFlagRequest(factIds));

    private async Task<Browser> SignedInToNewOrganizationAsync() =>
        await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
