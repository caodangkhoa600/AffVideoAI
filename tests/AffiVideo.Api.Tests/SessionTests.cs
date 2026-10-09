using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

public sealed class SessionTests(AffiVideoApp app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_seeded_demonstration_Owner_signs_in_with_the_credentials_in_the_README()
    {
        using var browser = app.NewBrowser();

        var response = await browser.SignInAsync("owner@demo.affivideo.local", "demo-owner-password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(AffiVideoApp.Json, Cancellation);
        Assert.Equal("owner@demo.affivideo.local", session!.Member.Email);
        Assert.Equal(MemberRole.Owner, session.Member.Role);
        Assert.Equal("AffiVideo Demo", session.Organization.Name);
    }

    [Fact]
    public async Task Seeding_again_leaves_the_demonstration_Organization_as_it_is()
    {
        var createdAnything = await app.SeedAsync();

        Assert.False(createdAnything);
        using var browser = app.NewBrowser();
        var response = await browser.SignInAsync("owner@demo.affivideo.local", "demo-owner-password");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_member_is_told_who_they_are_and_which_Organization_they_are_in()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = await app.SignedInAsync(organization.Owner);

        var session = await browser.GetAsync<SessionResponse>("/api/v1/session");

        Assert.Equal(organization.Owner.Email, session.Member.Email);
        Assert.Equal(MemberRole.Owner, session.Member.Role);
        Assert.Equal(organization.Id, session.Organization.Id);
    }

    [Fact]
    public async Task The_email_is_matched_whatever_its_letter_case()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.SignInAsync(organization.Owner.Email.ToUpperInvariant(), organization.Owner.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_starts_no_session()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.SignInAsync(organization.Owner.Email, "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_email_is_refused_in_the_same_words_as_a_wrong_password()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var unknown = await browser.SignInAsync("nobody@example.test", "some-long-password");
        var wrong = await browser.SignInAsync(organization.Owner.Email, "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await Title(wrong), await Title(unknown));
    }

    [Fact]
    public async Task After_five_wrong_passwords_the_right_one_is_refused_too()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await browser.SignInAsync(organization.Owner.Email, "not-the-password");
        }

        var response = await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/session")]
    [InlineData("/api/v1/organizations/{organization}")]
    [InlineData("/api/v1/organizations/{organization}/members")]
    [InlineData("/api/v1/organizations/{organization}/audit-log")]
    public async Task A_request_with_no_session_is_refused(string path)
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.GetAsync(path.Replace("{organization}", organization.Id.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_add_a_member_without_being_signed_in()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.PostAsync(
            $"/api/v1/organizations/{organization.Id}/members",
            new AddMemberRequest("newcomer@example.test", "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = await app.SignedInAsync(organization.Owner);

        var signedOut = await browser.DeleteAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_cannot_be_read_by_scripts_and_is_only_sent_securely_and_same_site()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("affivideo_session="));
        var attributes = cookie.Split(';').Skip(1).Select(a => a.Trim().ToLowerInvariant()).ToList();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
    }

    [Fact]
    public async Task Signing_in_gives_the_browser_nothing_but_cookies_scripts_cannot_read()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var token = await browser.GetAsync("/api/v1/antiforgery-token");
        var signIn = await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password);

        var cookies = token.Headers.GetValues("Set-Cookie").Concat(signIn.Headers.GetValues("Set-Cookie")).ToList();
        Assert.All(cookies, cookie => Assert.Contains("httponly", cookie.ToLowerInvariant()));
        Assert.All(cookies, cookie => Assert.Contains("secure", cookie.ToLowerInvariant()));
    }

    // The web app passes requests on to the API over plain HTTP inside Docker.
    [Fact]
    public async Task Cookies_are_marked_Secure_even_when_the_API_itself_is_reached_over_plain_HTTP()
    {
        using var http = app.CreateClient();

        var response = await http.GetAsync("/api/v1/antiforgery-token", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie.ToLowerInvariant()));
    }

    [Fact]
    public async Task Signing_in_without_an_anti_forgery_token_is_refused()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = app.NewBrowser();

        var response = await browser.Http.PostAsJsonAsync(
            "/api/v1/session",
            new SignInRequest(organization.Owner.Email, organization.Owner.Password),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/session")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_request_that_changes_something_is_refused_without_an_anti_forgery_token()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = await app.SignedInAsync(organization.Owner);
        var members = $"/api/v1/organizations/{organization.Id}/members";

        var response = await browser.Http.PostAsJsonAsync(
            members, new AddMemberRequest("newcomer@example.test", "a-long-enough-password"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await browser.GetAsync<PagedResponse<MemberResponse>>(members);
        Assert.DoesNotContain(list.Items, member => member.Email == "newcomer@example.test");
    }

    [Fact]
    public async Task An_anti_forgery_token_issued_to_someone_else_is_refused()
    {
        var organization = await app.CreateOrganizationAsync();
        using var browser = await app.SignedInAsync(organization.Owner);
        using var attacker = app.NewBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/session");
        request.Headers.Add(Browser.AntiforgeryHeader, await attacker.AntiforgeryTokenAsync());

        var response = await browser.Http.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/session")).StatusCode);
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>(Cancellation))?.Title;

    private sealed record Problem(string? Title);
}
