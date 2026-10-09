using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AffiVideo.Api.Tests;

public sealed class FactTests(AffiVideoApp app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_Fact_is_Proposed_and_shows_its_language_and_source()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var added = await member.PostAsync(
            Facts(product), new FactRequest("  Pin dùng liên tục 30 giờ  ", "vi", "  Hộp sản phẩm, mặt sau  "));

        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var fact = await ReadAsync<FactResponse>(added);
        Assert.Equal($"{Facts(product)}/{fact.Id}", added.Headers.Location?.OriginalString);

        var read = await member.GetAsync<FactResponse>($"{Facts(product)}/{fact.Id}");
        Assert.Equal(product, read.ProductId);
        Assert.Equal("Pin dùng liên tục 30 giờ", read.Text);
        Assert.Equal("vi", read.Language);
        Assert.Equal("Hộp sản phẩm, mặt sau", read.Source);
        Assert.Equal(FactState.Proposed, read.State);
        Assert.Null(read.ConfirmedByMemberId);
        Assert.Null(read.ConfirmedByEmail);
        Assert.Null(read.ConfirmedAt);
        Assert.Null(read.WithdrawnAt);
    }

    [Fact]
    public async Task A_Fact_needs_no_source()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var none = await AddAsync(member, product, new FactRequest("Charges over USB-C", "en"));
        var blank = await AddAsync(member, product, new FactRequest("Weighs 45 g", "en", "   "));

        Assert.Null(none.Source);
        Assert.Null(blank.Source);
    }

    [Fact]
    public async Task A_Fact_with_no_text_an_unknown_language_or_too_long_a_source_is_refused_with_the_reason_for_each_field()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);

        var response = await member.PostAsync(Facts(product), new FactRequest("   ", "tlh", new string('s', 501)));
        var tooLong = await member.PostAsync(Facts(product), new FactRequest(new string('t', 501), "vi"));
        var wrongCase = await member.PostAsync(Facts(product), new FactRequest("Charges over USB-C", "VI"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["language", "source", "text"], await RefusedFieldsAsync(response));
        Assert.Equal(["text"], await RefusedFieldsAsync(tooLong));
        Assert.Equal(["language"], await RefusedFieldsAsync(wrongCase));
        Assert.Equal(0, (await ListAsync(member, product)).Total);
    }

    [Fact]
    public async Task Confirming_a_Fact_records_who_confirmed_it_and_when()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var editorCredentials = await app.AddEditorAsync(owner, organization);
        using var editor = await app.SignedInAsync(editorCredentials);
        var editorId = (await editor.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var product = await NewProductAsync(owner);
        var fact = await AddAsync(owner, product);
        var before = DateTimeOffset.UtcNow;

        var confirmed = await ConfirmAsync(editor, product, fact.Id);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(FactState.Confirmed, (await ReadAsync<FactResponse>(confirmed)).State);
        var read = await owner.GetAsync<FactResponse>($"{Facts(product)}/{fact.Id}");
        Assert.Equal(FactState.Confirmed, read.State);
        Assert.Equal(editorId, read.ConfirmedByMemberId);
        Assert.Equal(editorCredentials.Email, read.ConfirmedByEmail);
        Assert.InRange(read.ConfirmedAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(fact.Text, read.Text);
    }

    [Fact]
    public async Task A_Proposed_Fact_can_be_Withdrawn_and_so_can_a_Confirmed_one_which_keeps_who_confirmed_it()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var proposed = await AddAsync(member, product);
        var confirmed = await AddAsync(member, product);
        (await ConfirmAsync(member, product, confirmed.Id)).EnsureSuccessStatusCode();
        var before = DateTimeOffset.UtcNow;

        var first = await WithdrawAsync(member, product, proposed.Id);
        var second = await WithdrawAsync(member, product, confirmed.Id);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var wasProposed = await member.GetAsync<FactResponse>($"{Facts(product)}/{proposed.Id}");
        Assert.Equal(FactState.Withdrawn, wasProposed.State);
        Assert.InRange(wasProposed.WithdrawnAt!.Value, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Null(wasProposed.ConfirmedAt);
        var wasConfirmed = await member.GetAsync<FactResponse>($"{Facts(product)}/{confirmed.Id}");
        Assert.Equal(FactState.Withdrawn, wasConfirmed.State);
        Assert.NotNull(wasConfirmed.ConfirmedAt);
        Assert.NotNull(wasConfirmed.ConfirmedByEmail);
    }

    [Fact]
    public async Task No_other_change_of_state_is_allowed_and_a_refused_one_changes_nothing()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var confirmed = await AddAsync(member, product);
        (await ConfirmAsync(member, product, confirmed.Id)).EnsureSuccessStatusCode();
        var confirmedAt = (await member.GetAsync<FactResponse>($"{Facts(product)}/{confirmed.Id}")).ConfirmedAt;
        var withdrawn = await AddAsync(member, product);
        (await WithdrawAsync(member, product, withdrawn.Id)).EnsureSuccessStatusCode();
        var withdrawnAt = (await member.GetAsync<FactResponse>($"{Facts(product)}/{withdrawn.Id}")).WithdrawnAt;

        var confirmAgain = await ConfirmAsync(member, product, confirmed.Id);
        var confirmWithdrawn = await ConfirmAsync(member, product, withdrawn.Id);
        var withdrawAgain = await WithdrawAsync(member, product, withdrawn.Id);
        var replaceWithdrawn = await ReplaceAsync(member, product, withdrawn.Id, new FactRequest("Brought back", "en"));

        Assert.Equal(HttpStatusCode.Conflict, confirmAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, confirmWithdrawn.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withdrawAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replaceWithdrawn.StatusCode);
        Assert.Contains("Withdrawn", (await ReadAsync<ProblemDetails>(confirmWithdrawn)).Detail);
        var facts = await ListAsync(member, product);
        Assert.Equal(2, facts.Total);
        Assert.Equal(confirmedAt, facts.Items.Single(f => f.Id == confirmed.Id).ConfirmedAt);
        var stillWithdrawn = facts.Items.Single(f => f.Id == withdrawn.Id);
        Assert.Equal(FactState.Withdrawn, stillWithdrawn.State);
        Assert.Equal(withdrawnAt, stillWithdrawn.WithdrawnAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editing_a_Fact_withdraws_it_and_creates_a_new_Proposed_one(bool confirmedFirst)
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var old = await AddAsync(member, product, new FactRequest("Pin 20 giờ", "vi", "Trang sản phẩm"));
        if (confirmedFirst) (await ConfirmAsync(member, product, old.Id)).EnsureSuccessStatusCode();

        var replaced = await ReplaceAsync(member, product, old.Id, new FactRequest("Pin 30 giờ", "vi", "Hộp sản phẩm"));

        Assert.Equal(HttpStatusCode.Created, replaced.StatusCode);
        var created = await ReadAsync<FactResponse>(replaced);
        Assert.Equal($"{Facts(product)}/{created.Id}", replaced.Headers.Location?.OriginalString);
        Assert.NotEqual(old.Id, created.Id);
        Assert.Equal("Pin 30 giờ", created.Text);
        Assert.Equal("Hộp sản phẩm", created.Source);
        Assert.Equal(FactState.Proposed, created.State);
        Assert.Null(created.ConfirmedAt);
        var after = await member.GetAsync<FactResponse>($"{Facts(product)}/{old.Id}");
        Assert.Equal(FactState.Withdrawn, after.State);
        Assert.Equal("Pin 20 giờ", after.Text);
        Assert.Equal("Trang sản phẩm", after.Source);
        Assert.Equal(2, (await ListAsync(member, product)).Total);
    }

    [Fact]
    public async Task A_Fact_cannot_be_edited_in_place()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var fact = await AddAsync(member, product);
        var path = $"{Facts(product)}/{fact.Id}";

        var put = await member.PutAsync(path, new FactRequest("Changed in place", "en"));
        using var patch = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(new { text = "Changed in place" }) };
        patch.Headers.Add(Browser.AntiforgeryHeader, await member.AntiforgeryTokenAsync());
        var patched = await member.Http.SendAsync(patch, Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patched.StatusCode);
        Assert.Equal(fact.Text, (await member.GetAsync<FactResponse>(path)).Text);
    }

    [Fact]
    public async Task An_invalid_edit_is_refused_and_the_Fact_is_not_withdrawn()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var fact = await AddAsync(member, product);

        var response = await ReplaceAsync(member, product, fact.Id, new FactRequest("", "vi"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["text"], await RefusedFieldsAsync(response));
        var only = Assert.Single((await ListAsync(member, product)).Items);
        Assert.Equal(FactState.Proposed, only.State);
    }

    [Fact]
    public async Task The_Facts_of_a_Product_are_listed_oldest_first_by_state_and_a_page_at_a_time()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var other = await NewProductAsync(member);
        var first = await AddAsync(member, product, new FactRequest("First", "en"));
        var second = await AddAsync(member, product, new FactRequest("Second", "en"));
        var third = await AddAsync(member, product, new FactRequest("Third", "en"));
        await AddAsync(member, other, new FactRequest("Of another Product", "en"));
        (await ConfirmAsync(member, product, second.Id)).EnsureSuccessStatusCode();
        (await WithdrawAsync(member, product, third.Id)).EnsureSuccessStatusCode();

        var all = await ListAsync(member, product);
        var proposed = await ListAsync(member, product, "?state=Proposed");
        var confirmed = await ListAsync(member, product, "?state=Confirmed");
        var withdrawn = await ListAsync(member, product, "?state=Withdrawn");
        var secondPage = await ListAsync(member, product, "?page=2&pageSize=2");
        var noSuchState = await member.GetAsync($"{Facts(product)}?state=Rejected");
        var numberedState = await member.GetAsync($"{Facts(product)}?state=7");

        Assert.Equal(["First", "Second", "Third"], all.Items.Select(f => f.Text));
        Assert.Equal(3, all.Total);
        Assert.Equal([first.Id], proposed.Items.Select(f => f.Id));
        Assert.Equal([second.Id], confirmed.Items.Select(f => f.Id));
        Assert.Equal(1, confirmed.Total);
        Assert.NotNull(confirmed.Items[0].ConfirmedByEmail);
        Assert.Equal([third.Id], withdrawn.Items.Select(f => f.Id));
        Assert.Equal([third.Id], secondPage.Items.Select(f => f.Id));
        Assert.Equal(3, secondPage.Total);
        Assert.Equal(HttpStatusCode.BadRequest, noSuchState.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numberedState.StatusCode);
    }

    [Fact]
    public async Task Confirming_and_withdrawing_are_written_to_the_audit_log_and_adding_is_not()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var ownerId = (await owner.GetAsync<SessionResponse>("/api/v1/session")).Member.Id;
        var product = await NewProductAsync(owner);
        var fact = await AddAsync(owner, product);
        var edited = await AddAsync(owner, product);
        var before = DateTimeOffset.UtcNow;

        (await ConfirmAsync(owner, product, fact.Id)).EnsureSuccessStatusCode();
        (await WithdrawAsync(owner, product, fact.Id)).EnsureSuccessStatusCode();
        (await ReplaceAsync(owner, product, edited.Id, new FactRequest("Said another way", "en"))).EnsureSuccessStatusCode();
        // Refused, so not recorded.
        await ConfirmAsync(owner, product, fact.Id);
        await WithdrawAsync(owner, product, fact.Id);

        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        Assert.Equal(
            [("fact.withdrawn", edited.Id), ("fact.withdrawn", fact.Id), ("fact.confirmed", fact.Id)],
            log.Items.Select(e => (e.Action, e.SubjectId!.Value)));
        Assert.All(log.Items, entry =>
        {
            Assert.Equal(ownerId, entry.ActorMemberId);
            Assert.Equal(organization.Id, entry.OrganizationId);
            Assert.InRange(entry.OccurredAt, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
        });
    }

    [Fact]
    public async Task When_two_members_confirm_the_same_Fact_at_once_one_is_recorded_and_the_other_refused()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));
        var product = await NewProductAsync(owner);
        var fact = await AddAsync(owner, product);

        // The tokens are fetched beforehand, so that the requests themselves leave together.
        var ownerToken = await owner.AntiforgeryTokenAsync();
        var editorToken = await editor.AntiforgeryTokenAsync();

        var answers = await Task.WhenAll(Enumerable.Range(0, 16).Select(turn => Task.Run(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Facts(product)}/{fact.Id}/confirm");
            request.Headers.Add(Browser.AntiforgeryHeader, turn % 2 == 0 ? ownerToken : editorToken);
            return await (turn % 2 == 0 ? owner : editor).Http.SendAsync(request, Cancellation);
        })));

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        Assert.Equal(15, answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict));
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == "fact.confirmed");
        Assert.Equal((await owner.GetAsync<FactResponse>($"{Facts(product)}/{fact.Id}")).ConfirmedByMemberId, entry.ActorMemberId);
    }

    [Fact]
    public async Task A_Fact_is_only_found_under_the_Product_it_belongs_to()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var other = await NewProductAsync(member);
        var fact = await AddAsync(member, product);

        var read = await member.GetAsync($"{Facts(other)}/{fact.Id}");
        var confirm = await ConfirmAsync(member, other, fact.Id);
        var withdraw = await WithdrawAsync(member, other, fact.Id);
        var replace = await ReplaceAsync(member, other, fact.Id, new FactRequest("Moved", "en"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, replace.StatusCode);
        Assert.Equal(FactState.Proposed, (await member.GetAsync<FactResponse>($"{Facts(product)}/{fact.Id}")).State);
        Assert.Equal(0, (await ListAsync(member, other)).Total);
    }

    [Fact]
    public async Task Facts_of_a_Product_that_does_not_exist_are_not_found()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var nothing = Guid.NewGuid();

        var list = await member.GetAsync(Facts(nothing));
        var add = await member.PostAsync(Facts(nothing), Valid());
        var read = await member.GetAsync($"{Facts(product)}/{nothing}");
        var confirm = await ConfirmAsync(member, product, nothing);

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, confirm.StatusCode);
    }

    [Fact]
    public async Task Facts_are_refused_to_someone_who_is_not_signed_in()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        var fact = await AddAsync(member, product);
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync(Facts(product));
        var add = await stranger.PostAsync(Facts(product), Valid());
        var confirm = await ConfirmAsync(stranger, product, fact.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, add.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, confirm.StatusCode);
        Assert.Equal(FactState.Proposed, (await member.GetAsync<FactResponse>($"{Facts(product)}/{fact.Id}")).State);
    }

    [Fact]
    public async Task Facts_can_be_added_to_an_archived_Product()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await NewProductAsync(member);
        (await member.PostAsync($"/api/v1/products/{product}/archive", new { })).EnsureSuccessStatusCode();

        var fact = await AddAsync(member, product);

        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(member, product, fact.Id)).StatusCode);
    }

    [Fact]
    public async Task The_seed_gives_AirBeat_X1_three_Confirmed_Facts_in_Vietnamese_once()
    {
        using var owner = await app.SignedInAsync(new Credentials("owner@demo.affivideo.local", "demo-owner-password"));
        var airBeat = Assert.Single((await owner.GetAsync<PagedResponse<ProductResponse>>("/api/v1/products?search=AirBeat")).Items).Id;

        var seededAgain = await app.SeedAsync();

        Assert.False(seededAgain);
        var facts = await ListAsync(owner, airBeat);
        Assert.Equal(3, facts.Total);
        Assert.All(facts.Items, fact =>
        {
            Assert.Equal(FactState.Confirmed, fact.State);
            Assert.Equal("vi", fact.Language);
            Assert.Equal("owner@demo.affivideo.local", fact.ConfirmedByEmail);
            Assert.NotNull(fact.ConfirmedAt);
        });
        Assert.Equal(3, facts.Items.Select(fact => fact.Text).Distinct().Count());
    }

    internal static string Facts(Guid productId) => $"/api/v1/products/{productId}/facts";

    internal static FactRequest Valid() => new("Pin dùng liên tục 30 giờ", "vi", "Hộp sản phẩm");

    internal static async Task<FactResponse> AddAsync(Browser member, Guid productId, FactRequest? request = null)
    {
        var response = await member.PostAsync(Facts(productId), request ?? Valid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<FactResponse>(response);
    }

    internal static Task<HttpResponseMessage> ConfirmAsync(Browser member, Guid productId, Guid factId) =>
        member.PostAsync($"{Facts(productId)}/{factId}/confirm", new { });

    internal static Task<HttpResponseMessage> WithdrawAsync(Browser member, Guid productId, Guid factId) =>
        member.PostAsync($"{Facts(productId)}/{factId}/withdraw", new { });

    internal static Task<HttpResponseMessage> ReplaceAsync(Browser member, Guid productId, Guid factId, FactRequest request) =>
        member.PostAsync($"{Facts(productId)}/{factId}/replace", request);

    internal static Task<PagedResponse<FactResponse>> ListAsync(Browser member, Guid productId, string query = "") =>
        member.GetAsync<PagedResponse<FactResponse>>($"{Facts(productId)}{query}");

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
