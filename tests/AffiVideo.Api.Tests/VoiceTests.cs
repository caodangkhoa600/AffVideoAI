using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using static AffiVideo.Api.Tests.StillGenerationTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The voices narration can be spoken in: the short list held in configuration,
/// the one a Project's brief names, and the one an Organization's Projects get
/// when their brief names none.
/// </summary>
public sealed class VoiceTests(AffiVideoApp app)
{
    private const string Voices = "/api/v1/voices";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_voices_on_offer_are_those_configured_and_a_member_can_play_the_sample_of_one_that_has_it()
    {
        await using var api = app.With(TwoVoices());
        var (member, _) = await OwnerAsync(api);
        using var signedIn = member;

        var offered = await member.GetAsync<VoicesResponse>(Voices);
        var sample = await member.GetAsync($"{Voices}/sarah/sample");
        var none = await member.GetAsync($"{Voices}/minh/sample");
        var nobody = await member.GetAsync($"{Voices}/nobody/sample");

        Assert.Equal([("sarah", "Sarah", true), ("minh", "Minh", false)], offered.Voices.Select(voice => (voice.Id, voice.Name, voice.HasSample)));
        // Until an Owner chooses another, an Organization's voice is the first on the list.
        Assert.Equal("sarah", offered.DefaultVoice);
        Assert.Equal(HttpStatusCode.OK, sample.StatusCode);
        Assert.Equal("audio/mpeg", sample.Content.Headers.ContentType?.MediaType);
        // The sample is the file that was recorded for the voice: playing it asks no provider for anything.
        Assert.Equal(VariantAudioTests.Mp3(), await sample.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task A_Project_has_the_voice_its_brief_names_or_else_the_Organizations_which_an_Owner_sets()
    {
        await using var api = app.With(TwoVoices());
        var (member, organization) = await OwnerAsync(api);
        using var signedIn = member;
        var product = await NewProductAsync(member);
        var path = $"/api/v1/organizations/{organization.Id}";

        var first = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product));
        var named = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { Voice = "minh" });
        var unknown = await member.PostAsync(ProjectTests.Projects, ProjectTests.Valid(product) with { Voice = "nobody" });
        var noSuchDefault = await member.PutAsync(path, new UpdateOrganizationRequest("Tiệm Âm Thanh", DefaultVoice: "nobody"));
        var chosen = await member.PutAsync(path, new UpdateOrganizationRequest("Tiệm Âm Thanh", DefaultVoice: "minh"));
        var afterwards = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product));
        // Renaming says nothing about the voice, and leaves it as it is.
        var renamed = await member.PutAsync(path, new UpdateOrganizationRequest("Tiệm Âm Thanh Mới"));

        Assert.Equal(("sarah", "minh"), (first.Voice, named.Voice));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("voice", await unknown.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.BadRequest, noSuchDefault.StatusCode);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        Assert.Equal("minh", (await chosen.Content.ReadFromJsonAsync<OrganizationResponse>(AffiVideoApp.Json, Cancellation))!.DefaultVoice);
        Assert.Equal("minh", afterwards.Voice);
        Assert.Equal("minh", (await renamed.Content.ReadFromJsonAsync<OrganizationResponse>(AffiVideoApp.Json, Cancellation))!.DefaultVoice);
        Assert.Equal("minh", (await member.GetAsync<VoicesResponse>(Voices)).DefaultVoice);
        // A brief is fixed: the Project made before the Owner chose keeps the voice it was made with.
        Assert.Equal("sarah", (await member.GetAsync<ProjectResponse>($"{ProjectTests.Projects}/{first.Id}")).Voice);
    }

    [Fact]
    public async Task Speech_is_asked_for_in_the_voice_of_its_Project()
    {
        var settings = TwoVoices();
        foreach (var (key, value) in SpokenNarrationTests.FakeSpeech(ratePerCharacter: 0.001m)) settings[key] = value;
        await using var api = app.With(settings);
        var (member, _) = await OwnerAsync(api);
        using var signedIn = member;
        var product = await NewProductAsync(member);
        await UploadPhotoAsync(member, product);
        await ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product) with { Voice = "minh" });
        var variant = await VariantTests.AddAsync(member, project.Id);
        await GeneratedAsync(member, variant);

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, 1));
        var queued = await SpokenNarrationTests.ConfirmedSpeechAsync(member, variant, 1);

        Assert.Equal("minh", estimate.Voice);
        Assert.Equal(4, queued.Count);
        Assert.All(queued, speech => Assert.Equal("minh", speech.Voice));
    }

    // Two voices, the first with a recorded sample.
    private static Dictionary<string, string> TwoVoices() => new()
    {
        ["Generation:Voices:0:Id"] = "sarah",
        ["Generation:Voices:0:Name"] = "Sarah",
        ["Generation:Voices:0:Sample"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3"),
        ["Generation:Voices:1:Id"] = "minh",
        ["Generation:Voices:1:Name"] = "Minh",
    };

    // A browser of that API, signed in as the Owner of a new Organization.
    private async Task<(Browser Member, TestOrganization Organization)> OwnerAsync(WebApplicationFactory<Program> api)
    {
        var organization = await app.CreateOrganizationAsync();
        var browser = AffiVideoApp.NewBrowser(api);
        (await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password)).EnsureSuccessStatusCode();
        return (browser, organization);
    }
}
