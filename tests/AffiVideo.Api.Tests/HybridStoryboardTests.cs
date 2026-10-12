using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>What the planning engine gives a Variant that chose Hybrid, as a member sees it in the Storyboard.</summary>
public sealed class HybridStoryboardTests(AffiVideoApp app)
{
    [Fact]
    public async Task With_no_image_provider_a_Hybrid_Variant_is_planned_exactly_as_in_Product_Lock()
    {
        using var member = await app.SignedInAsync((await app.CreateOrganizationAsync()).Owner);
        var product = await NewProductAsync(member);
        await UploadPhotoAsync(member, product);
        await ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product));
        var productLock = await VariantTests.AddAsync(member, project.Id);

        var hybrid = await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        var storyboard = await GeneratedAsync(member, hybrid);

        Assert.Equal(RenderMode.ProductLock, productLock.RenderMode);
        Assert.Equal(RenderMode.Hybrid, hybrid.RenderMode);
        Assert.Equal(RenderMode.Hybrid, (await member.GetAsync<VariantResponse>($"{VariantTests.Variants(project.Id)}/{hybrid.Id}")).RenderMode);
        Assert.Equal(RenderMode.Hybrid, storyboard.RenderMode);
        Assert.Equal(
            [Technique.ImageMotion, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            storyboard.Scenes.Select(scene => scene.Technique));
        Assert.Equal(RenderMode.ProductLock, (await GeneratedAsync(member, productLock)).RenderMode);
    }

    [Fact]
    public async Task With_an_image_provider_the_Hook_Scene_of_a_Hybrid_Variant_is_a_generated_backdrop_described_by_its_creative_template()
    {
        await using var api = WithFakeImages(app);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        var product = (await ProductTests.CreateAsync(member, ProductTests.Valid(category: "Bình giữ nhiệt"))).Id;
        var photo = await UploadPhotoAsync(member, product);
        await ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { Audience = "Dân văn phòng 25-35 tuổi" });
        var hybrid = await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
        var productLock = await VariantTests.AddAsync(member, project.Id);

        var storyboard = await GeneratedAsync(member, hybrid);
        var locked = await GeneratedAsync(member, productLock);

        Assert.Equal(
            [Technique.GeneratedBackdrop, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            storyboard.Scenes.Select(scene => scene.Technique));
        var hook = storyboard.Scenes[0];
        // The Product's category and the audience are placed in the template's description whole.
        Assert.Contains("Bình giữ nhiệt", hook.Description);
        Assert.Contains("Dân văn phòng 25-35 tuổi", hook.Description);
        // The Product in the Scene is still its photo.
        Assert.Equal([photo.Id], hook.AssetIds);
        Assert.All(storyboard.Scenes.Skip(1), scene => Assert.Null(scene.Description));
        Assert.Equal(storyboard.Scenes, (await member.GetAsync<StoryboardResponse>($"{Storyboards(hybrid)}/1")).Scenes, SameScene);

        // Product Lock generates nothing, whatever there is to generate with.
        Assert.Equal(
            [Technique.ImageMotion, Technique.ImageMotion, Technique.TextAnimation, Technique.ImageMotion],
            locked.Scenes.Select(scene => scene.Technique));
        Assert.All(locked.Scenes, scene => Assert.Null(scene.Description));
    }

    [Fact]
    public async Task A_members_edit_replaces_the_description_of_a_generated_Scene_in_the_next_version()
    {
        await using var api = WithFakeImages(app);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        var hybrid = await HybridVariantAsync(member);
        var first = await GeneratedAsync(member, hybrid);

        var second = await StoryboardEditTests.EditedAsync(
            member, hybrid, 1, StoryboardEditTests.Changing(first, new SceneEditRequest(1, Description: "  Bàn gỗ sồi bên cửa sổ, nắng sớm.  ")));
        var onPhoto = await StoryboardEditTests.EditAsync(
            member, hybrid, 2, StoryboardEditTests.Changing(second, new SceneEditRequest(2, Description: "Bãi biển lúc hoàng hôn")));
        var emptied = await StoryboardEditTests.EditAsync(
            member, hybrid, 2, StoryboardEditTests.Changing(second, new SceneEditRequest(1, Description: "   ")));

        Assert.Equal(2, second.Version);
        Assert.Equal("Bàn gỗ sồi bên cửa sổ, nắng sớm.", second.Scenes[0].Description);
        // A description is not the Scene's text: the Scene still says what its Facts say.
        Assert.False(second.Scenes[0].ManuallyEdited);
        Assert.Equal(Technique.GeneratedBackdrop, second.Scenes[0].Technique);
        // The version that was edited keeps the creative template's description.
        Assert.Equal(first.Scenes[0].Description, (await member.GetAsync<StoryboardResponse>($"{Storyboards(hybrid)}/1")).Scenes[0].Description);

        // Only a generated Scene has a picture to describe, and it has to be described.
        Assert.Equal(System.Net.HttpStatusCode.Conflict, onPhoto.StatusCode);
        Assert.Contains("Scene 2", await onPhoto.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, emptied.StatusCode);
        Assert.Contains("Scene 1", await emptied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, (await ListAsync(member, hybrid)).Total);
    }

    /// <summary>A Hybrid Variant of a Product that has what a Storyboard needs: a photo and a Confirmed Fact.</summary>
    internal static async Task<VariantResponse> HybridVariantAsync(Browser member)
    {
        var product = await NewProductAsync(member);
        await UploadPhotoAsync(member, product);
        await ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product));
        return await VariantTests.AddAsync(member, project.Id, VariantTests.Valid() with { RenderMode = RenderMode.Hybrid });
    }

    private static readonly IEqualityComparer<SceneResponse> SameScene = EqualityComparer<SceneResponse>.Create(
        (a, b) => System.Text.Json.JsonSerializer.Serialize(a, AffiVideoApp.Json) == System.Text.Json.JsonSerializer.Serialize(b, AffiVideoApp.Json));

    /// <summary>The API on the same database and storage, with the fake image provider configured.</summary>
    internal static WebApplicationFactory<Program> WithFakeImages(AffiVideoApp app) =>
        app.With(new Dictionary<string, string> { ["Generation:ImageProvider"] = "Fake" });

    /// <summary>A browser of that API, signed in as the Owner of a new Organization.</summary>
    internal static async Task<Browser> SignedInToNewOrganizationAsync(AffiVideoApp app, WebApplicationFactory<Program> api)
    {
        var organization = await app.CreateOrganizationAsync();
        var browser = AffiVideoApp.NewBrowser(api);
        (await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password)).EnsureSuccessStatusCode();
        return browser;
    }
}
