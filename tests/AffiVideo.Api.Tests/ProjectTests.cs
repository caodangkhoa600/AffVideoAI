using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

public sealed class ProjectTests(AffiVideoApp app)
{
    internal const string Projects = "/api/v1/projects";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_creates_a_Project_from_a_Product_and_reads_it_back()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Lumo 500"));
        var before = DateTimeOffset.UtcNow;

        var created = await member.PostAsync(Projects, new ProjectRequest(
            ProductId: product.Id,
            Audience: "  Dân văn phòng 25–35 tuổi  ",
            Language: "vi",
            TargetDurationSeconds: 20,
            Objective: "  Bấm vào liên kết trong phần mô tả  "));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await ReadAsync<ProjectResponse>(created);
        Assert.Equal($"{Projects}/{project.Id}", created.Headers.Location?.OriginalString);

        var read = await member.GetAsync<ProjectResponse>($"{Projects}/{project.Id}");
        Assert.Equal(product.Id, read.ProductId);
        Assert.Equal("Lumo 500", read.ProductName);
        Assert.Equal("Dân văn phòng 25–35 tuổi", read.Audience);
        Assert.Equal("vi", read.Language);
        Assert.Equal(20, read.TargetDurationSeconds);
        Assert.Equal("Bấm vào liên kết trong phần mô tả", read.Objective);
        Assert.Equal(0, read.VariantCount);
        Assert.InRange(read.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Theory]
    [InlineData(15, HttpStatusCode.Created)]
    [InlineData(30, HttpStatusCode.Created)]
    [InlineData(14, HttpStatusCode.BadRequest)]
    [InlineData(31, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task The_target_duration_is_from_15_to_30_seconds(int seconds, HttpStatusCode expected)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var response = await member.PostAsync(Projects, Valid(product) with { TargetDurationSeconds = seconds });

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal(["targetDurationSeconds"], await RefusedFieldsAsync(response));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("VI")]
    [InlineData("fr")]
    [InlineData("")]
    public async Task The_language_of_a_Project_is_Vietnamese(string language)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var response = await member.PostAsync(Projects, Valid(product) with { Language = language });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["language"], await RefusedFieldsAsync(response));
        Assert.Equal(0, (await ListAsync(member)).Total);
    }

    [Fact]
    public async Task A_Project_with_no_audience_or_too_long_an_objective_is_refused_with_the_reason_for_each_field()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var response = await member.PostAsync(
            Projects, Valid(product) with { Audience = "   ", Objective = new string('o', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["audience", "objective"], await RefusedFieldsAsync(response));
    }

    [Fact]
    public async Task A_Project_cannot_be_created_from_a_Product_that_does_not_exist()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var response = await member.PostAsync(Projects, Valid(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["productId"], await RefusedFieldsAsync(response));
        Assert.Equal(0, (await ListAsync(member)).Total);
    }

    [Fact]
    public async Task Projects_are_listed_newest_first_by_Product_and_a_page_at_a_time_with_their_number_of_Variants()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var bottle = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Bottle"));
        var lamp = await ProductTests.CreateAsync(member, ProductTests.Valid(name: "Lamp"));
        var first = await CreateAsync(member, Valid(bottle.Id) with { Objective = "First" });
        var second = await CreateAsync(member, Valid(lamp.Id) with { Objective = "Second" });
        var third = await CreateAsync(member, Valid(bottle.Id) with { Objective = "Third" });
        await VariantTests.AddAsync(member, first.Id);
        await VariantTests.AddAsync(member, first.Id, new VariantRequest(CreativeTemplate.LuxuryCinematic, "Another Hook"));

        var all = await ListAsync(member);
        var ofBottle = await ListAsync(member, $"?productId={bottle.Id}");
        var secondPage = await ListAsync(member, "?page=2&pageSize=2");
        var ofNothing = await ListAsync(member, $"?productId={Guid.NewGuid()}");

        Assert.Equal([third.Id, second.Id, first.Id], all.Items.Select(p => p.Id));
        Assert.Equal(["Bottle", "Lamp", "Bottle"], all.Items.Select(p => p.ProductName));
        Assert.Equal([0, 0, 2], all.Items.Select(p => p.VariantCount));
        Assert.Equal(3, all.Total);
        Assert.Equal([third.Id, first.Id], ofBottle.Items.Select(p => p.Id));
        Assert.Equal(2, ofBottle.Total);
        Assert.Equal([first.Id], secondPage.Items.Select(p => p.Id));
        Assert.Equal(3, secondPage.Total);
        Assert.Empty(ofNothing.Items);
    }

    [Fact]
    public async Task Deleting_a_Project_removes_it_and_its_Variants_and_is_written_to_the_audit_log()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var ownerId = (await owner.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var product = await NewProductAsync(owner);
        var project = await CreateAsync(owner, Valid(product));
        var kept = await CreateAsync(owner, Valid(product));
        var variant = await VariantTests.AddAsync(owner, project.Id);
        var keptVariant = await VariantTests.AddAsync(owner, kept.Id);

        var deleted = await owner.DeleteAsync($"{Projects}/{project.Id}");
        var again = await owner.DeleteAsync($"{Projects}/{project.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Projects}/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(VariantTests.Variants(project.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{VariantTests.Variants(project.Id)}/{variant.Id}")).StatusCode);
        Assert.Equal([kept.Id], (await ListAsync(owner)).Items.Select(p => p.Id));
        Assert.Equal([keptVariant.Id], (await VariantTests.ListAsync(owner, kept.Id)).Items.Select(v => v.Id));
        // The Product is not the Project's to delete.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/products/{product}")).StatusCode);
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items);
        Assert.Equal("project.deleted", entry.Action);
        Assert.Equal(project.Id, entry.SubjectId);
        Assert.Equal(ownerId, entry.ActorMemberId);
    }

    [Fact]
    public async Task When_a_Project_is_deleted_many_times_at_once_it_is_recorded_once()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var project = await CreateAsync(owner, Valid(await NewProductAsync(owner)));
        await VariantTests.AddAsync(owner, project.Id);
        var token = await owner.AntiforgeryTokenAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Projects}/{project.Id}");
            request.Headers.Add(Browser.AntiforgeryHeader, token);
            return await owner.Http.SendAsync(request, Cancellation);
        })));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.NoContent);
        Assert.Equal(7, answers.Count(answer => answer.StatusCode == HttpStatusCode.NotFound));
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        Assert.Single(log.Items);
    }

    [Fact]
    public async Task The_brief_of_a_Project_cannot_be_changed()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var project = await CreateAsync(member, Valid(product));

        var put = await member.PutAsync($"{Projects}/{project.Id}", Valid(product) with { TargetDurationSeconds = 30 });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(20, (await member.GetAsync<ProjectResponse>($"{Projects}/{project.Id}")).TargetDurationSeconds);
    }

    [Fact]
    public async Task A_Project_that_does_not_exist_is_not_found()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var read = await member.GetAsync($"{Projects}/{Guid.NewGuid()}");
        var delete = await member.DeleteAsync($"{Projects}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Projects_are_refused_to_someone_who_is_not_signed_in()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var project = await CreateAsync(member, Valid(product));
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync(Projects);
        var read = await stranger.GetAsync($"{Projects}/{project.Id}");
        var create = await stranger.PostAsync(Projects, Valid(product));
        var delete = await stranger.DeleteAsync($"{Projects}/{project.Id}");

        Assert.All([list, read, create, delete], response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(1, (await ListAsync(member)).Total);
    }

    [Fact]
    public async Task A_Project_can_be_created_from_an_archived_Product()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        (await member.PostAsync($"/api/v1/products/{product}/archive", new { })).EnsureSuccessStatusCode();

        var response = await member.PostAsync(Projects, Valid(product));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    internal static ProjectRequest Valid(Guid productId) => new(
        ProductId: productId,
        Audience: "Dân văn phòng",
        Language: "vi",
        TargetDurationSeconds: 20,
        Objective: "Bấm vào liên kết");

    internal static async Task<ProjectResponse> CreateAsync(Browser member, ProjectRequest request)
    {
        var response = await member.PostAsync(Projects, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ProjectResponse>(response);
    }

    internal static Task<PagedResponse<ProjectResponse>> ListAsync(Browser member, string query = "") =>
        member.GetAsync<PagedResponse<ProjectResponse>>($"{Projects}{query}");

    private static async Task<Guid> NewProductAsync(Browser member) => (await ProductTests.CreateAsync(member, ProductTests.Valid())).Id;

    private async Task<Browser> SignedInToNewOrganizationAsync() =>
        await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

    // The fields a 400 names, as the web app's form calls them.
    private static async Task<string[]> RefusedFieldsAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        return problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
