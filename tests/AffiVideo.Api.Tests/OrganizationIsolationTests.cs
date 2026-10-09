using System.Net;
using AffiVideo.Contracts;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Two Organizations, and a member of one asking for the other's records by
/// identifier. Later tickets add what their Organizations own to this file.
/// </summary>
public sealed class OrganizationIsolationTests(AffiVideoApp app)
{
    [Theory]
    [InlineData("")]
    [InlineData("/members")]
    [InlineData("/audit-log")]
    public async Task An_Owner_of_one_Organization_is_refused_what_belongs_to_another(string resource)
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);

        var response = await me.GetAsync($"/api/v1/organizations/{theirs.Id}{resource}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_Organization_that_belongs_to_someone_else_looks_the_same_as_one_that_does_not_exist()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);

        var someoneElses = await me.GetAsync($"/api/v1/organizations/{theirs.Id}/members");
        var nobodys = await me.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}/members");

        Assert.Equal(nobodys.StatusCode, someoneElses.StatusCode);
    }

    [Fact]
    public async Task An_Owner_of_one_Organization_cannot_change_the_settings_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var path = $"/api/v1/organizations/{theirs.Id}";
        var before = await them.GetAsync<OrganizationResponse>(path);

        var response = await me.PutAsync(path, new UpdateOrganizationRequest("Taken over"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before.Name, (await them.GetAsync<OrganizationResponse>(path)).Name);
    }

    [Fact]
    public async Task An_Owner_of_one_Organization_cannot_add_a_member_to_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var path = $"/api/v1/organizations/{theirs.Id}/members";

        var response = await me.PostAsync(path, new AddMemberRequest($"intruder-{Guid.NewGuid():N}@example.test", "a-long-enough-password"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, (await them.GetAsync<PagedResponse<MemberResponse>>(path)).Total);
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_reading_editing_and_archiving_a_Product_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var product = await ProductTests.CreateAsync(them, ProductTests.Valid(name: "Theirs"));
        var path = $"/api/v1/products/{product.Id}";

        var read = await me.GetAsync(path);
        var edit = await me.PutAsync(path, ProductTests.Valid(name: "Taken over"));
        var archive = await me.PostAsync($"{path}/archive", new { });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archive.StatusCode);
        var after = await them.GetAsync<ProductResponse>(path);
        Assert.Equal("Theirs", after.Name);
        Assert.Equal(Domain.ProductStatus.Active, after.Status);
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_the_assets_and_files_of_a_Product_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var image = ProductAssetTests.Image(SkiaSharp.SKEncodedImageFormat.Png, 8, 8);
        var theirProduct = (await ProductTests.CreateAsync(them, ProductTests.Valid(name: "Theirs"))).Id;
        var theirAsset = await ProductAssetTests.UploadedAsync(them, theirProduct, Domain.ProductAssetKind.Photo, image);
        var myProduct = (await ProductTests.CreateAsync(me, ProductTests.Valid(name: "Mine"))).Id;

        var list = await me.GetAsync(ProductAssetTests.Assets(theirProduct));
        var file = await me.GetAsync(ProductAssetTests.Content(theirProduct, theirAsset.Id));
        var fileUnderMyProduct = await me.GetAsync(ProductAssetTests.Content(myProduct, theirAsset.Id));
        var upload = await ProductAssetTests.UploadAsync(me, theirProduct, Domain.ProductAssetKind.Photo, image);
        var remove = await me.DeleteAsync($"{ProductAssetTests.Assets(theirProduct)}/{theirAsset.Id}");
        var removeUnderMyProduct = await me.DeleteAsync($"{ProductAssetTests.Assets(myProduct)}/{theirAsset.Id}");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fileUnderMyProduct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removeUnderMyProduct.StatusCode);
        Assert.Equal([theirAsset.Id], (await them.GetAsync<ProductAssetResponse[]>(ProductAssetTests.Assets(theirProduct))).Select(a => a.Id));
        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync(ProductAssetTests.Content(theirProduct, theirAsset.Id))).StatusCode);
        Assert.Empty(await app.StoredKeysAsync($"organizations/{mine.Id}/"));
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_reading_and_changing_the_Facts_of_a_Product_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var theirProduct = (await ProductTests.CreateAsync(them, ProductTests.Valid(name: "Theirs"))).Id;
        var theirFact = await FactTests.AddAsync(them, theirProduct);
        var myProduct = (await ProductTests.CreateAsync(me, ProductTests.Valid(name: "Mine"))).Id;

        var list = await me.GetAsync(FactTests.Facts(theirProduct));
        var read = await me.GetAsync($"{FactTests.Facts(theirProduct)}/{theirFact.Id}");
        var readUnderMyProduct = await me.GetAsync($"{FactTests.Facts(myProduct)}/{theirFact.Id}");
        var add = await me.PostAsync(FactTests.Facts(theirProduct), FactTests.Valid());
        var confirm = await FactTests.ConfirmAsync(me, theirProduct, theirFact.Id);
        var confirmUnderMyProduct = await FactTests.ConfirmAsync(me, myProduct, theirFact.Id);
        var withdraw = await FactTests.WithdrawAsync(me, theirProduct, theirFact.Id);
        var withdrawUnderMyProduct = await FactTests.WithdrawAsync(me, myProduct, theirFact.Id);
        var replace = await FactTests.ReplaceAsync(me, theirProduct, theirFact.Id, FactTests.Valid());
        var replaceUnderMyProduct = await FactTests.ReplaceAsync(me, myProduct, theirFact.Id, FactTests.Valid());

        Assert.All(
            [list, read, readUnderMyProduct, add, confirm, confirmUnderMyProduct, withdraw, withdrawUnderMyProduct, replace, replaceUnderMyProduct],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var after = Assert.Single((await FactTests.ListAsync(them, theirProduct)).Items);
        Assert.Equal(theirFact.Id, after.Id);
        Assert.Equal(Domain.FactState.Proposed, after.State);
        Assert.Equal(0, (await FactTests.ListAsync(me, myProduct)).Total);
        Assert.Empty((await me.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{mine.Id}/audit-log")).Items);
        Assert.Empty((await them.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{theirs.Id}/audit-log")).Items);
    }

    [Fact]
    public async Task A_Product_list_and_its_categories_hold_only_what_belongs_to_that_Organization()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        await ProductTests.CreateAsync(them, ProductTests.Valid(name: "Theirs", category: "Their category"));
        await ProductTests.CreateAsync(me, ProductTests.Valid(name: "Mine", category: "My category"));

        var products = await me.GetAsync<PagedResponse<ProductResponse>>("/api/v1/products?search=");
        var categories = await me.GetAsync<string[]>("/api/v1/products/categories");

        Assert.Equal(["Mine"], products.Items.Select(p => p.Name));
        Assert.Equal(1, products.Total);
        Assert.Equal(["My category"], categories);
    }

    [Fact]
    public async Task A_member_list_holds_only_the_members_of_that_Organization()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        await app.AddEditorAsync(them, theirs);

        var members = await me.GetAsync<PagedResponse<MemberResponse>>($"/api/v1/organizations/{mine.Id}/members");

        Assert.Equal([mine.Owner.Email], members.Items.Select(m => m.Email));
    }
}
