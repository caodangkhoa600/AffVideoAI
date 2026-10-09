using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

public sealed class OrganizationTests(AffiVideoApp app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_Owner_adds_an_Editor_who_can_then_sign_in_to_the_same_Organization()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var editor = new Credentials($"editor-{Guid.NewGuid():N}@example.test", "the-editors-password");

        var added = await owner.PostAsync(
            $"/api/v1/organizations/{organization.Id}/members", new AddMemberRequest(editor.Email, editor.Password));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var member = await added.Content.ReadFromJsonAsync<MemberResponse>(AffiVideoApp.Json, Cancellation);
        Assert.Equal(editor.Email, member!.Email);
        Assert.Equal(MemberRole.Editor, member.Role);

        using var asEditor = await app.SignedInAsync(editor);
        var session = await asEditor.GetAsync<SessionResponse>("/api/v1/session");
        Assert.Equal(MemberRole.Editor, session.Member.Role);
        Assert.Equal(organization.Id, session.Organization.Id);
    }

    [Fact]
    public async Task The_member_list_shows_the_Owner_and_the_Editors_they_added()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var editor = await app.AddEditorAsync(owner, organization);

        var members = await owner.GetAsync<PagedResponse<MemberResponse>>($"/api/v1/organizations/{organization.Id}/members");

        Assert.Equal(2, members.Total);
        Assert.Equal(
            new[] { (editor.Email, MemberRole.Editor), (organization.Owner.Email, MemberRole.Owner) }.OrderBy(m => m.Email),
            members.Items.Select(m => (m.Email, m.Role)));
    }

    [Fact]
    public async Task The_member_list_is_served_a_page_at_a_time()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        await app.AddEditorAsync(owner, organization);
        await app.AddEditorAsync(owner, organization);
        var path = $"/api/v1/organizations/{organization.Id}/members?pageSize=2";

        var first = await owner.GetAsync<PagedResponse<MemberResponse>>($"{path}&page=1");
        var second = await owner.GetAsync<PagedResponse<MemberResponse>>($"{path}&page=2");

        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(m => m.Id));
    }

    [Fact]
    public async Task A_page_far_past_the_end_of_the_member_list_is_empty()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);

        var page = await owner.GetAsync<PagedResponse<MemberResponse>>(
            $"/api/v1/organizations/{organization.Id}/members?page={int.MaxValue}&pageSize=200");

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task An_Editor_sees_the_Organization_and_its_members_but_is_refused_managing_either()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var path = $"/api/v1/organizations/{organization.Id}";

        var read = await editor.GetAsync(path);
        var list = await editor.GetAsync($"{path}/members");
        var rename = await editor.PutAsync(path, new UpdateOrganizationRequest("Renamed by an Editor"));
        var add = await editor.PostAsync($"{path}/members", new AddMemberRequest("friend@example.test", "a-long-enough-password"));
        var audit = await editor.GetAsync($"{path}/audit-log");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);
        var after = await owner.GetAsync<OrganizationResponse>(path);
        Assert.NotEqual("Renamed by an Editor", after.Name);
        var members = await owner.GetAsync<PagedResponse<MemberResponse>>($"{path}/members");
        Assert.DoesNotContain(members.Items, m => m.Email == "friend@example.test");
    }

    [Fact]
    public async Task An_Owner_renames_the_Organization()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var path = $"/api/v1/organizations/{organization.Id}";

        var renamed = await owner.PutAsync(path, new UpdateOrganizationRequest("  Tiệm Âm Thanh  "));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Tiệm Âm Thanh", (await owner.GetAsync<OrganizationResponse>(path)).Name);
        Assert.Equal("Tiệm Âm Thanh", (await owner.GetAsync<SessionResponse>("/api/v1/session")).Organization.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")] // 101 characters
    public async Task An_Organization_name_that_is_blank_or_too_long_is_refused(string name)
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);

        var response = await owner.PutAsync($"/api/v1/organizations/{organization.Id}", new UpdateOrganizationRequest(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Name", await ErrorFields(response));
    }

    [Fact]
    public async Task Adding_a_member_is_recorded_in_the_audit_log_with_who_did_it_where_and_when()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var ownerId = (await owner.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var path = $"/api/v1/organizations/{organization.Id}";
        var before = DateTimeOffset.UtcNow;

        var added = await owner.PostAsync(
            $"{path}/members", new AddMemberRequest($"editor-{Guid.NewGuid():N}@example.test", "the-editors-password"));
        var editor = await added.Content.ReadFromJsonAsync<MemberResponse>(AffiVideoApp.Json, Cancellation);

        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"{path}/audit-log");
        var entry = Assert.Single(log.Items);
        Assert.Equal("member.added", entry.Action);
        Assert.Equal(ownerId, entry.ActorMemberId);
        Assert.Equal(organization.Owner.Email, entry.ActorEmail);
        Assert.Equal(organization.Id, entry.OrganizationId);
        Assert.Equal(editor!.Id, entry.SubjectId);
        Assert.InRange(entry.OccurredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task The_audit_log_lists_the_newest_action_first()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var path = $"/api/v1/organizations/{organization.Id}";
        var first = await AddAsync(owner, path);
        var second = await AddAsync(owner, path);

        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"{path}/audit-log");

        Assert.Equal(new Guid?[] { second.Id, first.Id }, log.Items.Select(e => e.SubjectId));
    }

    [Fact]
    public async Task An_email_that_already_belongs_to_a_member_is_refused_and_nothing_is_recorded()
    {
        var organization = await app.CreateOrganizationAsync();
        var elsewhere = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var path = $"/api/v1/organizations/{organization.Id}";

        var response = await owner.PostAsync(
            $"{path}/members", new AddMemberRequest(elsewhere.Owner.Email, "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["email"], await ErrorFields(response));
        Assert.Empty((await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"{path}/audit-log")).Items);
    }

    [Theory]
    [InlineData("not-an-email", "a-long-enough-password", "email")]
    [InlineData("newcomer@example.test", "too-short", "password")]
    public async Task A_member_with_a_bad_email_or_a_short_password_is_refused(string email, string password, string field)
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var path = $"/api/v1/organizations/{organization.Id}/members";

        var response = await owner.PostAsync(path, new AddMemberRequest(email, password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([field], await ErrorFields(response));
        Assert.Equal(1, (await owner.GetAsync<PagedResponse<MemberResponse>>(path)).Total);
    }

    private static async Task<MemberResponse> AddAsync(Browser owner, string organizationPath)
    {
        var added = await owner.PostAsync(
            $"{organizationPath}/members", new AddMemberRequest($"editor-{Guid.NewGuid():N}@example.test", "the-editors-password"));
        return (await added.Content.ReadFromJsonAsync<MemberResponse>(AffiVideoApp.Json, Cancellation))!;
    }

    private static async Task<IEnumerable<string>> ErrorFields(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblem>(Cancellation))!.Errors.Keys;

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
