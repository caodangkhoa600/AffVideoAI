using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

public sealed class ProductTests(AffiVideoApp app)
{
    private const string Products = "/api/v1/products";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_creates_a_Product_and_reads_it_back()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var request = new ProductRequest(
            Name: "Bình giữ nhiệt Lumo 500",
            Category: "Đồ gia dụng",
            Brand: "Lumo",
            Description: "Bình thép không gỉ hai lớp, 500 ml.",
            OriginalUrl: "https://shop.example/lumo-500",
            TargetAudience: "Dân văn phòng",
            Price: 349000.50m,
            Currency: "VND",
            AffiliateUrl: "https://aff.example/r/abc?id=1",
            Tags: ["bình nước", "văn phòng"]);

        var created = await member.PostAsync(Products, request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await ReadAsync<ProductResponse>(created);
        Assert.Equal($"{Products}/{product.Id}", created.Headers.Location?.OriginalString);

        var read = await member.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Equal("Bình giữ nhiệt Lumo 500", read.Name);
        Assert.Equal("Đồ gia dụng", read.Category);
        Assert.Equal("Lumo", read.Brand);
        Assert.Equal("Bình thép không gỉ hai lớp, 500 ml.", read.Description);
        Assert.Equal("https://shop.example/lumo-500", read.OriginalUrl);
        Assert.Equal("Dân văn phòng", read.TargetAudience);
        Assert.Equal(349000.50m, read.Price);
        Assert.Equal("VND", read.Currency);
        Assert.Equal("https://aff.example/r/abc?id=1", read.AffiliateUrl);
        Assert.Equal(["bình nước", "văn phòng"], read.Tags);
        Assert.Equal(ProductStatus.Active, read.Status);
    }

    [Fact]
    public async Task A_Product_needs_no_price_affiliate_URL_or_tags()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var product = await CreateAsync(member, Valid() with { Price = null, Currency = null, AffiliateUrl = null, Tags = null });
        var blank = await CreateAsync(member, Valid() with { AffiliateUrl = "  " });

        var read = await member.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Null(read.Price);
        Assert.Null(read.Currency);
        Assert.Null(read.AffiliateUrl);
        Assert.Empty(read.Tags);
        Assert.Null(blank.AffiliateUrl);
    }

    [Fact]
    public async Task A_Product_that_does_not_exist_is_not_found()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var response = await member.GetAsync($"{Products}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Products_are_refused_to_someone_who_is_not_signed_in()
    {
        using var stranger = app.NewBrowser();

        var list = await stranger.GetAsync(Products);
        var create = await stranger.PostAsync(Products, Valid());

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_refused_with_the_reason_for_each_field()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var request = Valid() with
        {
            Name = "   ",
            Category = new string('c', 101),
            OriginalUrl = "not a web address",
            AffiliateUrl = "javascript:alert(1)",
            Price = -1m,
            Currency = "dong",
            Tags = [new string('t', 51)],
        };

        var response = await member.PostAsync(Products, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["affiliateUrl", "category", "currency", "name", "originalUrl", "price", "tags"],
            await RefusedFieldsAsync(response));
        Assert.Equal(0, (await member.GetAsync<PagedResponse<ProductResponse>>(Products)).Total);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://shop.example/lumo")]
    [InlineData("/lumo-500")]
    [InlineData("file:///etc/passwd")]
    public async Task A_Product_URL_must_be_a_full_web_address(string url)
    {
        using var member = await SignedInToNewOrganizationAsync();

        var response = await member.PostAsync(Products, Valid() with { OriginalUrl = url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["originalUrl"], await RefusedFieldsAsync(response));
    }

    [Fact]
    public async Task A_price_without_a_currency_is_refused_and_so_is_a_currency_without_a_price()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var noCurrency = await member.PostAsync(Products, Valid() with { Price = 10m, Currency = null });
        var noPrice = await member.PostAsync(Products, Valid() with { Price = null, Currency = "VND" });

        Assert.Equal(HttpStatusCode.BadRequest, noCurrency.StatusCode);
        Assert.Equal(["currency"], await RefusedFieldsAsync(noCurrency));
        Assert.Equal(HttpStatusCode.BadRequest, noPrice.StatusCode);
        Assert.Equal(["price"], await RefusedFieldsAsync(noPrice));
    }

    [Fact]
    public async Task A_price_finer_than_two_decimal_places_is_refused_rather_than_rounded()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var response = await member.PostAsync(Products, Valid() with { Price = 9.999m, Currency = "USD" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["price"], await RefusedFieldsAsync(response));
    }

    [Fact]
    public async Task A_price_keeps_its_exact_decimal_value_and_its_currency()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var small = await CreateAsync(member, Valid() with { Price = 0.10m, Currency = "USD" });
        var large = await CreateAsync(member, Valid() with { Price = 9999999999999.99m, Currency = "VND" });

        var readSmall = await member.GetAsync<ProductResponse>($"{Products}/{small.Id}");
        Assert.Equal(0.10m, readSmall.Price);
        Assert.Equal("USD", readSmall.Currency);
        Assert.Equal(9999999999999.99m, (await member.GetAsync<ProductResponse>($"{Products}/{large.Id}")).Price);
    }

    [Fact]
    public async Task A_Product_URL_is_kept_exactly_as_typed_even_when_nothing_answers_at_it()
    {
        using var member = await SignedInToNewOrganizationAsync();
        const string original = "HTTPS://No-Such-Host.invalid:8443/Path/?utm_source=x&b=%20#Frag";
        const string affiliate = "http://127.0.0.1:1/internal";

        var product = await CreateAsync(member, Valid() with { OriginalUrl = original, AffiliateUrl = affiliate });

        var read = await member.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Equal(original, read.OriginalUrl);
        Assert.Equal(affiliate, read.AffiliateUrl);
    }

    [Fact]
    public async Task Surrounding_spaces_are_dropped_and_a_repeated_tag_is_kept_once()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var product = await CreateAsync(member, Valid() with { Name = "  Lumo 500  ", Tags = [" bottle ", "Bottle", "", "steel"] });

        Assert.Equal("Lumo 500", product.Name);
        Assert.Equal(["bottle", "steel"], product.Tags);
    }

    [Fact]
    public async Task A_member_edits_a_Product()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await CreateAsync(member, Valid());
        var changed = new ProductRequest(
            Name: "Lumo 750",
            Category: "Kitchen",
            Brand: "Lumo Pro",
            Description: "Bigger.",
            OriginalUrl: "https://shop.example/lumo-750",
            TargetAudience: "Hikers",
            Tags: ["steel", "750ml"]);

        var response = await member.PutAsync($"{Products}/{product.Id}", changed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await member.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Equal("Lumo 750", read.Name);
        Assert.Equal("Kitchen", read.Category);
        Assert.Equal("Lumo Pro", read.Brand);
        Assert.Equal("Bigger.", read.Description);
        Assert.Equal("https://shop.example/lumo-750", read.OriginalUrl);
        Assert.Equal("Hikers", read.TargetAudience);
        Assert.Null(read.Price);
        Assert.Null(read.Currency);
        Assert.Null(read.AffiliateUrl);
        Assert.Equal(["steel", "750ml"], read.Tags);
        Assert.Equal(ProductStatus.Active, read.Status);
    }

    [Fact]
    public async Task An_invalid_edit_is_refused_and_changes_nothing()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await CreateAsync(member, Valid());

        var response = await member.PutAsync($"{Products}/{product.Id}", Valid() with { Name = "", Brand = "Changed" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["name"], await RefusedFieldsAsync(response));
        Assert.Equal("Lumo", (await member.GetAsync<ProductResponse>($"{Products}/{product.Id}")).Brand);
    }

    [Fact]
    public async Task Editing_or_archiving_a_Product_that_does_not_exist_is_not_found()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var edit = await member.PutAsync($"{Products}/{Guid.NewGuid()}", Valid());
        var archive = await ArchiveAsync(member, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
    }

    [Fact]
    public async Task A_member_archives_a_Product_which_is_kept_and_can_still_be_read()
    {
        using var member = await SignedInToNewOrganizationAsync();
        var product = await CreateAsync(member, Valid());

        var response = await ArchiveAsync(member, product.Id);
        var again = await ArchiveAsync(member, product.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var read = await member.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Equal(ProductStatus.Archived, read.Status);
        Assert.Equal("Lumo 500", read.Name);
    }

    [Fact]
    public async Task An_Editor_does_everything_with_Products_that_an_Owner_does()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        using var editor = await app.SignedInAsync(await app.AddEditorAsync(owner, organization));

        var product = await CreateAsync(editor, Valid());
        var edit = await editor.PutAsync($"{Products}/{product.Id}", Valid(name: "Renamed"));
        var archive = await ArchiveAsync(editor, product.Id);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var seenByOwner = await owner.GetAsync<ProductResponse>($"{Products}/{product.Id}");
        Assert.Equal("Renamed", seenByOwner.Name);
        Assert.Equal(ProductStatus.Archived, seenByOwner.Status);
    }

    [Fact]
    public async Task The_list_is_in_name_order_and_holds_archived_Products_too()
    {
        using var member = await SignedInToNewOrganizationAsync();
        await CreateAsync(member, Valid(name: "Cup"));
        await CreateAsync(member, Valid(name: "Bottle"));
        var archived = await CreateAsync(member, Valid(name: "Apron"));
        await ArchiveAsync(member, archived.Id);

        var list = await member.GetAsync<PagedResponse<ProductResponse>>(Products);

        Assert.Equal(3, list.Total);
        Assert.Equal(["Apron", "Bottle", "Cup"], list.Items.Select(p => p.Name));
    }

    [Theory]
    [InlineData("lumo", new[] { "Lumo 500", "Mini LUMO" })]
    [InlineData("  500 ", new[] { "Lumo 500" })]
    [InlineData("ình", new[] { "Bình 100%" })]
    [InlineData("100%", new[] { "Bình 100%" })]
    [InlineData("%", new[] { "Bình 100%" })]
    [InlineData("_", new string[0])]
    [InlineData("kettle", new string[0])]
    public async Task The_list_is_searched_by_name_in_any_letter_case(string search, string[] expected)
    {
        using var member = await SignedInToNewOrganizationAsync();
        await CreateAsync(member, Valid(name: "Lumo 500"));
        await CreateAsync(member, Valid(name: "Mini LUMO"));
        await CreateAsync(member, Valid(name: "Bình 100%"));

        var list = await member.GetAsync<PagedResponse<ProductResponse>>($"{Products}?search={Uri.EscapeDataString(search)}");

        Assert.Equal(expected, list.Items.Select(p => p.Name));
        Assert.Equal(expected.Length, list.Total);
    }

    [Fact]
    public async Task The_list_is_filtered_by_category_and_by_status_and_by_both_with_a_search()
    {
        using var member = await SignedInToNewOrganizationAsync();
        await CreateAsync(member, Valid(name: "Lumo 500", category: "Home"));
        await CreateAsync(member, Valid(name: "Lumo Kettle", category: "Kitchen"));
        var old = await CreateAsync(member, Valid(name: "Lumo 250", category: "Home"));
        await CreateAsync(member, Valid(name: "Vase", category: "Home"));
        await ArchiveAsync(member, old.Id);

        var home = await member.GetAsync<PagedResponse<ProductResponse>>($"{Products}?category=Home");
        var active = await member.GetAsync<PagedResponse<ProductResponse>>($"{Products}?status=Active");
        var archived = await member.GetAsync<PagedResponse<ProductResponse>>($"{Products}?status=Archived");
        var all = await member.GetAsync<PagedResponse<ProductResponse>>($"{Products}?search=lumo&category=Home&status=Active");

        Assert.Equal(["Lumo 250", "Lumo 500", "Vase"], home.Items.Select(p => p.Name));
        Assert.Equal(["Lumo 500", "Lumo Kettle", "Vase"], active.Items.Select(p => p.Name));
        Assert.Equal(["Lumo 250"], archived.Items.Select(p => p.Name));
        Assert.Equal(["Lumo 500"], all.Items.Select(p => p.Name));
        Assert.Equal(1, all.Total);
    }

    [Fact]
    public async Task A_status_that_does_not_exist_is_refused()
    {
        using var member = await SignedInToNewOrganizationAsync();

        var response = await member.GetAsync($"{Products}?status=Lost");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_list_is_served_a_page_at_a_time_and_counts_only_what_matches()
    {
        using var member = await SignedInToNewOrganizationAsync();
        foreach (var name in new[] { "Lumo A", "Lumo B", "Lumo C", "Vase" }) await CreateAsync(member, Valid(name: name));
        var path = $"{Products}?search=lumo&pageSize=2";

        var first = await member.GetAsync<PagedResponse<ProductResponse>>($"{path}&page=1");
        var second = await member.GetAsync<PagedResponse<ProductResponse>>($"{path}&page=2");

        Assert.Equal(3, first.Total);
        Assert.Equal(["Lumo A", "Lumo B"], first.Items.Select(p => p.Name));
        Assert.Equal(["Lumo C"], second.Items.Select(p => p.Name));
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
    }

    [Fact]
    public async Task The_categories_in_use_are_listed_once_each()
    {
        using var member = await SignedInToNewOrganizationAsync();
        await CreateAsync(member, Valid(category: "Kitchen"));
        await CreateAsync(member, Valid(category: "Home"));
        await CreateAsync(member, Valid(category: "Home"));

        var categories = await member.GetAsync<string[]>($"{Products}/categories");

        Assert.Equal(["Home", "Kitchen"], categories);
    }

    [Fact]
    public async Task The_seed_adds_the_fictional_AirBeat_X1_to_the_demonstration_Organization_once()
    {
        using var owner = await app.SignedInAsync(new Credentials("owner@demo.affivideo.local", "demo-owner-password"));

        var seededAgain = await app.SeedAsync();

        Assert.False(seededAgain);
        var products = await owner.GetAsync<PagedResponse<ProductResponse>>($"{Products}?search=AirBeat");
        var airBeat = Assert.Single(products.Items);
        Assert.Equal("AirBeat X1", airBeat.Name);
        Assert.Equal(ProductStatus.Active, airBeat.Status);
    }

    private static Task<HttpResponseMessage> ArchiveAsync(Browser member, Guid productId) =>
        member.PostAsync($"{Products}/{productId}/archive", new { });

    // The fields a 400 names, as the web app's form calls them.
    private static async Task<string[]> RefusedFieldsAsync(HttpResponseMessage response)
    {
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        return problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<Browser> SignedInToNewOrganizationAsync() =>
        await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);

    internal static ProductRequest Valid(string name = "Lumo 500", string category = "Home") => new(
        Name: name,
        Category: category,
        Brand: "Lumo",
        Description: "A double-walled steel bottle.",
        OriginalUrl: "https://shop.example/lumo-500",
        TargetAudience: "Office workers",
        Price: 349000m,
        Currency: "VND",
        AffiliateUrl: "https://aff.example/r/abc",
        Tags: ["bottle"]);

    internal static async Task<ProductResponse> CreateAsync(Browser member, ProductRequest request)
    {
        var response = await member.PostAsync(Products, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<ProductResponse>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
