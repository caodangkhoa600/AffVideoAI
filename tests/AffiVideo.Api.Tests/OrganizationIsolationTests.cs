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
    public async Task A_member_of_one_Organization_is_refused_the_Projects_and_Variants_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var theirProduct = (await ProductTests.CreateAsync(them, ProductTests.Valid(name: "Theirs"))).Id;
        var theirProject = await ProjectTests.CreateAsync(them, ProjectTests.Valid(theirProduct));
        var theirVariant = await VariantTests.AddAsync(them, theirProject.Id);
        var myProduct = (await ProductTests.CreateAsync(me, ProductTests.Valid(name: "Mine"))).Id;
        var myProject = await ProjectTests.CreateAsync(me, ProjectTests.Valid(myProduct));
        var theirProjectPath = $"{ProjectTests.Projects}/{theirProject.Id}";

        var read = await me.GetAsync(theirProjectPath);
        var delete = await me.DeleteAsync(theirProjectPath);
        var listVariants = await me.GetAsync(VariantTests.Variants(theirProject.Id));
        var readVariant = await me.GetAsync($"{VariantTests.Variants(theirProject.Id)}/{theirVariant.Id}");
        var readVariantUnderMyProject = await me.GetAsync($"{VariantTests.Variants(myProject.Id)}/{theirVariant.Id}");
        var addVariant = await me.PostAsync(VariantTests.Variants(theirProject.Id), VariantTests.Valid());
        var duplicate = await VariantTests.DuplicateAsync(me, theirProject.Id, theirVariant.Id, "Taken over");
        var duplicateUnderMyProject = await VariantTests.DuplicateAsync(me, myProject.Id, theirVariant.Id, "Taken over");
        // A Project is made from a Product of the member's own Organization, or not at all.
        var fromTheirProduct = await me.PostAsync(ProjectTests.Projects, ProjectTests.Valid(theirProduct));
        var fromNoProduct = await me.PostAsync(ProjectTests.Projects, ProjectTests.Valid(Guid.NewGuid()));

        Assert.All(
            [read, delete, listVariants, readVariant, readVariantUnderMyProject, addVariant, duplicate, duplicateUnderMyProject],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, fromTheirProduct.StatusCode);
        Assert.Equal(fromNoProduct.StatusCode, fromTheirProduct.StatusCode);
        Assert.Equal([myProject.Id], (await ProjectTests.ListAsync(me)).Items.Select(p => p.Id));
        Assert.Empty((await ProjectTests.ListAsync(me, $"?productId={theirProduct}")).Items);
        Assert.Equal(0, (await VariantTests.ListAsync(me, myProject.Id)).Total);
        Assert.Equal([theirProject.Id], (await ProjectTests.ListAsync(them)).Items.Select(p => p.Id));
        Assert.Equal([theirVariant.Id], (await VariantTests.ListAsync(them, theirProject.Id)).Items.Select(v => v.Id));
        Assert.Empty((await them.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{theirs.Id}/audit-log")).Items);
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_reading_and_generating_the_Storyboards_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var theirVariant = await StoryboardTests.ReadyVariantAsync(them);
        var theirStoryboard = await StoryboardTests.GeneratedAsync(them, theirVariant);
        var myVariant = await StoryboardTests.ReadyVariantAsync(me);
        var theirVariantUnderMyProject = $"{VariantTests.Variants(myVariant.ProjectId)}/{theirVariant.Id}/storyboards";

        var list = await me.GetAsync(StoryboardTests.Storyboards(theirVariant));
        var read = await me.GetAsync($"{StoryboardTests.Storyboards(theirVariant)}/1");
        var generate = await StoryboardTests.GenerateAsync(me, theirVariant);
        var listUnderMyProject = await me.GetAsync(theirVariantUnderMyProject);
        var readUnderMyProject = await me.GetAsync($"{theirVariantUnderMyProject}/1");
        var generateUnderMyProject = await me.PostAsync(theirVariantUnderMyProject, new { });
        // My Variant has no Storyboard: version 1 under it must not find theirs.
        var readUnderMyVariant = await me.GetAsync($"{StoryboardTests.Storyboards(myVariant)}/1");

        Assert.All(
            [list, read, generate, listUnderMyProject, readUnderMyProject, generateUnderMyProject, readUnderMyVariant],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(0, (await StoryboardTests.ListAsync(me, myVariant)).Total);
        Assert.Equal([theirStoryboard.Id], (await StoryboardTests.ListAsync(them, theirVariant)).Items.Select(s => s.Id));
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_editing_the_Storyboards_of_another_and_showing_its_photos_in_their_own()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var theirProduct = await StoryboardTests.NewProductAsync(them);
        var theirPhoto = await StoryboardTests.UploadPhotoAsync(them, theirProduct);
        await StoryboardTests.ConfirmedFactAsync(them, theirProduct, "Pin dùng liên tục 30 giờ");
        var theirVariant = await StoryboardTests.NewVariantAsync(them, theirProduct);
        var theirStoryboard = await StoryboardTests.GeneratedAsync(them, theirVariant);
        var myVariant = await StoryboardTests.ReadyVariantAsync(me);
        var myStoryboard = await StoryboardTests.GeneratedAsync(me, myVariant);
        var edit = StoryboardEditTests.Changing(theirStoryboard, new SceneEditRequest(2, OnScreenText: ["Lumo"]));
        var theirVariantUnderMyProject = $"{VariantTests.Variants(myVariant.ProjectId)}/{theirVariant.Id}/storyboards/1";

        var edited = await StoryboardEditTests.EditAsync(me, theirVariant, 1, edit);
        var regenerated = await StoryboardEditTests.RegenerateAsync(me, theirVariant, 1, position: 2);
        var editedUnderMyProject = await me.PostAsync($"{theirVariantUnderMyProject}/edits", new StoryboardEditRequest(edit));
        var regeneratedUnderMyProject = await me.PostAsync($"{theirVariantUnderMyProject}/scenes/2/regenerate", new { });
        // Their photo is no asset of my Product, whatever its identifier.
        var withTheirPhoto = await StoryboardEditTests.EditAsync(
            me, myVariant, 1, StoryboardEditTests.Changing(myStoryboard, new SceneEditRequest(2, AssetId: theirPhoto.Id)));

        Assert.All(
            [edited, regenerated, editedUnderMyProject, regeneratedUnderMyProject],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(HttpStatusCode.Conflict, withTheirPhoto.StatusCode);
        Assert.Equal(1, (await StoryboardTests.ListAsync(me, myVariant)).Total);
        Assert.Equal([theirStoryboard.Id], (await StoryboardTests.ListAsync(them, theirVariant)).Items.Select(s => s.Id));
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_the_render_jobs_and_Rendered_Videos_of_another()
    {
        var theirs = await RenderTests.RenderedAsync(app);
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var myVariant = await StoryboardTests.ReadyVariantAsync(me);
        await StoryboardTests.GeneratedAsync(me, myVariant);
        var theirVariantUnderMyProject = $"{VariantTests.Variants(myVariant.ProjectId)}/{theirs.Variant.Id}/storyboards/1/renders";

        var submit = await RenderTests.SubmitAsync(me, RenderTests.Renders(theirs.Variant, 1));
        var list = await me.GetAsync(RenderTests.Renders(theirs.Variant, 1));
        var submitUnderMyProject = await RenderTests.SubmitAsync(me, theirVariantUnderMyProject);
        var listUnderMyProject = await me.GetAsync(theirVariantUnderMyProject);
        var job = await me.GetAsync($"/api/v1/render-jobs/{theirs.Job.Id}");
        var cancel = await me.PostAsync($"/api/v1/render-jobs/{theirs.Job.Id}/cancel", new { });
        var video = await me.GetAsync($"/api/v1/rendered-videos/{theirs.Video.Id}");
        var file = await me.GetAsync($"/api/v1/rendered-videos/{theirs.Video.Id}/content");

        Assert.All(
            [submit, list, submitUnderMyProject, listUnderMyProject, job, cancel, video, file],
            response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        // My own version 1 has no job: theirs is not listed under it, and none was queued for them.
        Assert.Equal(0, (await me.GetAsync<PagedResponse<RenderJobResponse>>(RenderTests.Renders(myVariant, 1))).Total);
        Assert.Equal(1, (await them.GetAsync<PagedResponse<RenderJobResponse>>(RenderTests.Renders(theirs.Variant, 1))).Total);
        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync($"/api/v1/rendered-videos/{theirs.Video.Id}/content")).StatusCode);
        Assert.Empty(await app.StoredKeysAsync($"organizations/{mine.Id}/rendered-videos/"));
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_approving_downloading_and_deleting_a_Rendered_Video_of_another()
    {
        var theirs = await RenderedVideoTests.LibraryAsync(app);
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        var waiting = $"{RenderedVideoTests.Videos}/{theirs.Flask.Id}";
        var approved = $"{RenderedVideoTests.Videos}/{theirs.Earbuds.Id}";

        HttpResponseMessage[] responses =
        [
            await RenderedVideoTests.ApproveAsync(me, theirs.Flask.Id),
            await FlaggedForReviewTests.ClearVideoAsync(me, theirs.Flask.Id, Guid.NewGuid()),
            await me.GetAsync($"{waiting}/download"),
            await me.GetAsync($"{approved}/download"),
            await me.GetAsync($"{approved}/content"),
            await me.DeleteAsync(waiting),
            await me.DeleteAsync(approved),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        // My library is empty, whatever I search for; theirs is as it was, files included.
        Assert.Equal(0, (await RenderedVideoTests.ListAsync(me)).Total);
        Assert.Equal(0, (await RenderedVideoTests.ListAsync(me, "?search=lumo&state=ReadyForReview")).Total);
        Assert.Equal(
            [(theirs.Flask.Id, Domain.RenderedVideoState.ReadyForReview), (theirs.Earbuds.Id, Domain.RenderedVideoState.Approved)],
            (await RenderedVideoTests.ListAsync(them)).Items.Select(video => (video.Id, video.State)));
        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync($"{approved}/download")).StatusCode);
        Assert.Equal(2, (await app.StoredKeysAsync($"organizations/{theirs.OrganizationId}/rendered-videos/")).Length);
        Assert.Empty((await me.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{mine.Id}/audit-log")).Items);
    }

    [Fact]
    public async Task A_member_of_one_Organization_is_refused_clearing_the_flags_of_another()
    {
        var theirs = await app.CreateOrganizationAsync();
        var mine = await app.CreateOrganizationAsync();
        using var me = await app.SignedInAsync(mine.Owner);
        using var them = await app.SignedInAsync(theirs.Owner);
        using var stranger = app.NewBrowser();
        var product = await StoryboardTests.NewProductAsync(them);
        await StoryboardTests.UploadPhotoAsync(them, product);
        var battery = await StoryboardTests.ConfirmedFactAsync(them, product, "Pin dùng liên tục 30 giờ");
        var variant = await StoryboardTests.NewVariantAsync(them, product);
        await StoryboardTests.GeneratedAsync(them, variant);
        (await FactTests.WithdrawAsync(them, product, battery.Id)).EnsureSuccessStatusCode();
        var myVariant = await StoryboardTests.ReadyVariantAsync(me);

        var cleared = await FlaggedForReviewTests.ClearStoryboardAsync(me, variant, 1, battery.Id);
        var underMyProject = await me.PostAsync(
            $"{VariantTests.Variants(myVariant.ProjectId)}/{variant.Id}/storyboards/1/clear-flag", new ClearFlagRequest([battery.Id]));
        var nobodys = await FlaggedForReviewTests.ClearStoryboardAsync(stranger, variant, 1, battery.Id);
        var noSuchVersion = await FlaggedForReviewTests.ClearStoryboardAsync(them, variant, 2, battery.Id);

        Assert.All([cleared, underMyProject, noSuchVersion], response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal(HttpStatusCode.Unauthorized, nobodys.StatusCode);
        Assert.Single((await them.GetAsync<StoryboardResponse>($"{StoryboardTests.Storyboards(variant)}/1")).Flags);
        var myLog = await me.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{mine.Id}/audit-log");
        var theirLog = await them.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{theirs.Id}/audit-log");
        Assert.DoesNotContain(myLog.Items.Concat(theirLog.Items), entry => entry.Action == "storyboard.flag-cleared");
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
