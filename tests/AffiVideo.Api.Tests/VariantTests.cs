using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

public sealed class VariantTests(AffiVideoApp app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_adds_Variants_to_a_Project_and_the_Project_lists_them_oldest_first()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var other = await NewProjectAsync(member);
        var before = DateTimeOffset.UtcNow;

        var added = await member.PostAsync(
            Variants(project), new VariantRequest(CreativeTemplate.ProductShowcase, "  Bạn vẫn dùng bình nhựa?  "));
        var second = await AddAsync(member, project, new VariantRequest(CreativeTemplate.LuxuryCinematic, "Giữ lạnh 24 giờ"));
        var third = await AddAsync(member, project, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?"));
        await AddAsync(member, other);

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var first = await ReadAsync<VariantResponse>(added);
        Assert.Equal($"{Variants(project)}/{first.Id}", added.Headers.Location?.OriginalString);
        var read = await member.GetAsync<VariantResponse>($"{Variants(project)}/{first.Id}");
        Assert.Equal(project, read.ProjectId);
        Assert.Equal(CreativeTemplate.ProductShowcase, read.CreativeTemplate);
        Assert.Equal("Bạn vẫn dùng bình nhựa?", read.Hook);
        Assert.InRange(read.CreatedAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));

        var listed = await ListAsync(member, project);
        Assert.Equal([first.Id, second.Id, third.Id], listed.Items.Select(v => v.Id));
        Assert.Equal(3, listed.Total);
        Assert.Equal([third.Id], (await ListAsync(member, project, "?page=2&pageSize=2")).Items.Select(v => v.Id));
        Assert.Equal(3, (await member.GetAsync<ProjectResponse>($"{ProjectTests.Projects}/{project}")).VariantCount);
    }

    [Theory]
    [InlineData("""{"creativeTemplate":"ProductShowcase"}""")]
    [InlineData("""{"creativeTemplate":"ProductShowcase","hook":"   "}""")]
    [InlineData("""{"hook":"Giữ lạnh 24 giờ"}""")]
    [InlineData("""{"creativeTemplate":"FeatureExplainer","hook":"Giữ lạnh 24 giờ"}""")]
    [InlineData("""{"creativeTemplate":7,"hook":"Giữ lạnh 24 giờ"}""")]
    // A creative template is named, never numbered.
    [InlineData("""{"creativeTemplate":0,"hook":"Giữ lạnh 24 giờ"}""")]
    [InlineData("""{"creativeTemplate":null,"hook":"Giữ lạnh 24 giờ"}""")]
    public async Task A_Variant_requires_a_creative_template_and_a_Hook(string body)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);

        var response = await PostJsonAsync(member, Variants(project), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await ListAsync(member, project)).Total);
    }

    [Fact]
    public async Task A_Hook_that_is_blank_or_too_long_is_refused_with_the_reason()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);

        var blank = await member.PostAsync(Variants(project), new VariantRequest(CreativeTemplate.ProductShowcase, "  "));
        var tooLong = await member.PostAsync(Variants(project), new VariantRequest(CreativeTemplate.ProductShowcase, new string('h', 201)));
        var longest = await member.PostAsync(Variants(project), new VariantRequest(CreativeTemplate.ProductShowcase, new string('h', 200)));

        Assert.Equal(["hook"], await RefusedFieldsAsync(blank));
        Assert.Equal(["hook"], await RefusedFieldsAsync(tooLong));
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
    }

    [Fact]
    public async Task Duplicating_a_Variant_with_a_new_Hook_creates_a_separate_Variant_with_the_same_creative_template()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var added = await AddAsync(member, project, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?"));
        var original = await member.GetAsync<VariantResponse>($"{Variants(project)}/{added.Id}");

        var duplicated = await DuplicateAsync(member, project, original.Id, "  Bình nào giữ nóng cả ngày?  ");

        Assert.Equal(HttpStatusCode.Created, duplicated.StatusCode);
        var copy = await ReadAsync<VariantResponse>(duplicated);
        Assert.Equal($"{Variants(project)}/{copy.Id}", duplicated.Headers.Location?.OriginalString);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(project, copy.ProjectId);
        Assert.Equal(CreativeTemplate.ProblemSolution, copy.CreativeTemplate);
        Assert.Equal("Bình nào giữ nóng cả ngày?", copy.Hook);
        var after = await member.GetAsync<VariantResponse>($"{Variants(project)}/{original.Id}");
        Assert.Equal(original, after);
        Assert.Equal([original.Id, copy.Id], (await ListAsync(member, project)).Items.Select(v => v.Id));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"hook":""}""")]
    [InlineData("""{"hook":"   "}""")]
    // The Hook it already has: a different Hook is what makes a different Variant.
    [InlineData("""{"hook":"Nước nguội sau một giờ?"}""")]
    [InlineData("""{"hook":"  NƯỚC NGUỘI sau một giờ? "}""")]
    public async Task Duplicating_a_Variant_asks_for_a_new_Hook(string body)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var original = await AddAsync(member, project, new VariantRequest(CreativeTemplate.ProblemSolution, "Nước nguội sau một giờ?"));

        var response = await PostJsonAsync(member, $"{Variants(project)}/{original.Id}/duplicate", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (body != "{}") Assert.Equal(["hook"], await RefusedFieldsAsync(response));
        Assert.Equal([original.Id], (await ListAsync(member, project)).Items.Select(v => v.Id));
    }

    [Fact]
    public async Task The_identifier_of_a_Variant_never_changes_and_nothing_changes_a_Variant()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var path = $"{Variants(project)}/{(await AddAsync(member, project)).Id}";
        var variant = await member.GetAsync<VariantResponse>(path);

        var put = await member.PutAsync(path, new VariantRequest(CreativeTemplate.LuxuryCinematic, "Changed in place"));
        var patched = await SendJsonAsync(member, HttpMethod.Patch, path, """{"hook":"Changed in place"}""");
        var delete = await member.DeleteAsync(path);
        (await DuplicateAsync(member, project, variant.Id, "A different Hook")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patched.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);
        Assert.Equal(variant, await member.GetAsync<VariantResponse>(path));
        Assert.Equal(variant, (await ListAsync(member, project)).Items[0]);
    }

    [Fact]
    public async Task A_Variant_is_only_found_under_the_Project_it_belongs_to()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var other = await NewProjectAsync(member);
        var variant = await AddAsync(member, project);

        var read = await member.GetAsync($"{Variants(other)}/{variant.Id}");
        var duplicate = await DuplicateAsync(member, other, variant.Id, "A different Hook");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, duplicate.StatusCode);
        Assert.Equal(0, (await ListAsync(member, other)).Total);
        Assert.Equal(1, (await ListAsync(member, project)).Total);
    }

    [Fact]
    public async Task Variants_of_a_Project_that_does_not_exist_are_not_found()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var nothing = Guid.NewGuid();

        var list = await member.GetAsync(Variants(nothing));
        var add = await member.PostAsync(Variants(nothing), Valid());
        var read = await member.GetAsync($"{Variants(project)}/{nothing}");
        var duplicate = await DuplicateAsync(member, project, nothing, "A different Hook");

        Assert.All([list, add, read, duplicate], response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [Fact]
    public async Task Variants_are_refused_to_someone_who_is_not_signed_in()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var project = await NewProjectAsync(member);
        var variant = await AddAsync(member, project);
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync(Variants(project));
        var add = await stranger.PostAsync(Variants(project), Valid());
        var duplicate = await DuplicateAsync(stranger, project, variant.Id, "A different Hook");

        Assert.All([list, add, duplicate], response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(1, (await ListAsync(member, project)).Total);
    }

    internal static string Variants(Guid projectId) => $"{ProjectTests.Projects}/{projectId}/variants";

    internal static VariantRequest Valid() => new(CreativeTemplate.ProductShowcase, "Bạn vẫn dùng bình nhựa?");

    internal static async Task<VariantResponse> AddAsync(Browser member, Guid projectId, VariantRequest? request = null)
    {
        var response = await member.PostAsync(Variants(projectId), request ?? Valid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<VariantResponse>(response);
    }

    internal static Task<HttpResponseMessage> DuplicateAsync(Browser member, Guid projectId, Guid variantId, string hook) =>
        member.PostAsync($"{Variants(projectId)}/{variantId}/duplicate", new DuplicateVariantRequest(hook));

    internal static Task<PagedResponse<VariantResponse>> ListAsync(Browser member, Guid projectId, string query = "") =>
        member.GetAsync<PagedResponse<VariantResponse>>($"{Variants(projectId)}{query}");

    private static async Task<Guid> NewProjectAsync(Browser member)
    {
        var product = await ProductTests.CreateAsync(member, ProductTests.Valid());
        return (await ProjectTests.CreateAsync(member, ProjectTests.Valid(product.Id))).Id;
    }

    private async Task<Browser> SignedInToNewOrganizationAsync() =>
        await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

    // A body the typed requests cannot express: a missing field, or a creative template that is not one.
    private static Task<HttpResponseMessage> PostJsonAsync(Browser member, string path, string body) =>
        SendJsonAsync(member, HttpMethod.Post, path, body);

    private static async Task<HttpResponseMessage> SendJsonAsync(Browser member, HttpMethod method, string path, string body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(Browser.AntiforgeryHeader, await member.AntiforgeryTokenAsync());
        return await member.Http.SendAsync(request, Cancellation);
    }

    // The fields a 400 names, as the web app's form calls them.
    private static async Task<string[]> RefusedFieldsAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        return problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
