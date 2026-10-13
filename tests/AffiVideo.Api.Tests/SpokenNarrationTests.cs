using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using static AffiVideo.Api.Tests.HybridStoryboardTests;
using static AffiVideo.Api.Tests.StillGenerationTests;
using static AffiVideo.Api.Tests.StoryboardTests;

namespace AffiVideo.Api.Tests;

/// <summary>
/// Narration spoken from each Scene's narration text, as a member meets it at
/// Storyboard review: the estimate, the confirmation, and the speech the fake
/// speech provider makes.
/// </summary>
public sealed class SpokenNarrationTests(AffiVideoApp app)
{
    // What a character spoken by the fake provider is taken to cost in these tests.
    private const decimal Rate = 0.001m;

    [Fact]
    public async Task The_estimate_names_the_speech_of_each_Scene_that_has_narration_text_and_what_it_will_cost()
    {
        await using var api = WithFakeSpeech(app, Rate);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        // Product Lock: speech does not wait for anything else to be generated.
        var locked = await SpokenVariantAsync(member);
        var storyboard = await GeneratedAsync(member, locked);

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, 1));

        Assert.True(estimate.Available);
        Assert.Null(estimate.Unavailable);
        Assert.Empty(estimate.Stills);
        Assert.Empty(estimate.Clips);
        Assert.Equal(SpeechProvider.Fake, estimate.SpeechProvider);
        // One piece of speech for each Scene, saying that Scene's narration text.
        Assert.Equal(
            storyboard.Scenes.Select(scene => (scene.Position, scene.NarrationText)),
            estimate.Speech.Select(speech => (speech.ScenePosition, speech.NarrationText)));
        // The Facts Scene says its one Fact, 25 characters with its full stop, at a tenth of a cent each.
        Assert.Equal("Pin dùng liên tục 30 giờ.", estimate.Speech[2].NarrationText);
        Assert.Equal(0.025m, estimate.Speech[2].EstimatedAmount);
        Assert.Equal(estimate.Speech.Sum(speech => speech.EstimatedAmount), estimate.Total);
        Assert.Equal((0m, "USD"), (estimate.Spent, estimate.Currency));
    }

    [Fact]
    public async Task The_worker_has_confirmed_speech_made_and_the_member_can_then_listen_to_it_and_see_its_cost()
    {
        await using var stack = await app.NewStackAsync(FakeSpeech(Rate));
        using var member = await stack.NewMemberAsync();
        var locked = await SpokenVariantAsync(member);
        await GeneratedAsync(member, locked);
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, 1));

        var queued = await ConfirmedSpeechAsync(member, locked, 1);
        var before = await member.GetAsync($"/api/v1/speech/{queued[2].Id}/content");
        var worker = await stack.StartWorkerAsync(FakeSpeechWorker);
        var spoken = new List<SpeechResponse>();
        foreach (var speech in queued) spoken.Add(await EndedAsync(member, speech, worker));

        // One piece for each Scene, queued with nothing made and nothing recorded yet.
        Assert.Equal(4, queued.Count);
        Assert.All(queued, speech => Assert.Equal((SpeechState.Queued, 0, null), (speech.State, speech.DurationMs, speech.Cost)));
        Assert.All(spoken, speech => Assert.Equal((SpeechState.Made, 1), (speech.State, speech.Attempt)));
        var facts = spoken[2];
        Assert.Null(facts.Failure);
        Assert.Equal(("Pin dùng liên tục 30 giờ.", "vi"), (facts.NarrationText, facts.Language));
        // What made it is in the data: the fake, by name.
        Assert.Equal((SpeechProvider.Fake, true, "fake-speech"), (facts.Provider, facts.Fake, facts.Model));
        // The fake's tone lasts as long as the text is estimated to take: six Vietnamese words at four a second.
        Assert.Equal(1500, facts.DurationMs);

        // There is no sound until it is made, and then it is a WAV of that length: 48,000 samples a second, two bytes each.
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        var content = await member.GetAsync($"/api/v1/speech/{facts.Id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("audio/wav", content.Content.Headers.ContentType?.MediaType);
        var wav = await content.Content.ReadAsByteArrayAsync(Cancellation);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + 72_000 * 2, wav.Length);

        // Its Production cost record: the estimate it was confirmed at, by what made it.
        Assert.NotNull(facts.Cost);
        Assert.Equal((0.025m, "USD", "1"), (facts.Cost.EstimatedAmount, facts.Cost.Currency, facts.Cost.RatesVersion));
        Assert.Equal((SpeechProvider.Fake, "fake-speech"), (facts.Cost.Provider, facts.Cost.Model));

        // Everything is spoken: there is nothing left to confirm, each Scene has its speech, and it counts as spent.
        var after = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, 1));
        Assert.Empty(after.Speech);
        Assert.Equal((0m, estimate.Total), (after.Total, after.Spent));
        Assert.Equal([1, 2, 3, 4], after.SceneSpeech.Select(has => has.ScenePosition));
        Assert.Equal(spoken.Select(speech => speech.Id), after.SceneSpeech.Select(has => has.Speech.Id));
        var nothingLeft = await member.PostAsync(Generation(locked, 1), new ConfirmGenerationRequest(0m), NewKey());
        Assert.Equal(HttpStatusCode.Conflict, nothingLeft.StatusCode);
    }

    [Fact]
    public async Task Only_a_Scene_whose_narration_text_changed_is_spoken_again_and_a_second_Variant_that_says_the_same_pays_nothing()
    {
        await using var stack = await app.NewStackAsync(FakeSpeech(Rate));
        using var member = await stack.NewMemberAsync();
        var locked = await SpokenVariantAsync(member);
        var first = await GeneratedAsync(member, locked);
        var worker = await stack.StartWorkerAsync(FakeSpeechWorker);
        var spoken = new List<SpeechResponse>();
        foreach (var speech in await ConfirmedSpeechAsync(member, locked, 1)) spoken.Add(await EndedAsync(member, speech, worker));

        // The reveal is given other words: twelve characters of them.
        await StoryboardEditTests.EditedAsync(
            member, locked, 1, StoryboardEditTests.Changing(first, new SceneEditRequest(2, NarrationText: "Gặp Lumo 500")));
        var edited = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, 2));
        // Another Variant of the Project, with the same Hook and creative template, says what the first one said.
        var twin = await VariantTests.AddAsync(member, locked.ProjectId);
        await GeneratedAsync(member, twin);
        var ofTwin = await member.GetAsync<GenerationEstimateResponse>(Generation(twin, 1));

        var again = Assert.Single(edited.Speech);
        Assert.Equal((2, "Gặp Lumo 500", 0.012m), (again.ScenePosition, again.NarrationText, again.EstimatedAmount));
        Assert.Equal(0.012m, edited.Total);
        // The other three Scenes keep the speech they had.
        Assert.Equal(
            new[] { (1, spoken[0].Id), (3, spoken[2].Id), (4, spoken[3].Id) },
            edited.SceneSpeech.Select(has => (has.ScenePosition, has.Speech.Id)));
        Assert.Empty(ofTwin.Speech);
        Assert.Equal(0m, ofTwin.Total);
        Assert.Equal(spoken.Select(speech => speech.Id), ofTwin.SceneSpeech.Select(has => has.Speech.Id));
    }

    [Fact]
    public async Task A_Variant_with_an_uploaded_narration_has_nothing_to_speak()
    {
        await using var api = WithFakeSpeech(app, Rate);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        var locked = await SpokenVariantAsync(member);
        await GeneratedAsync(member, locked);
        await VariantAudioTests.UploadedAsync(member, locked, VariantAudioKind.Narration, VariantAudioTests.Tone(seconds: 3));

        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, 1));
        var confirmed = await member.PostAsync(Generation(locked, 1), new ConfirmGenerationRequest(0m), NewKey());

        Assert.Empty(estimate.Speech);
        Assert.Equal(0m, estimate.Total);
        Assert.Equal(HttpStatusCode.Conflict, confirmed.StatusCode);
    }

    [Fact]
    public async Task The_estimate_warns_of_narration_text_that_looks_too_long_for_its_Scene_before_anything_is_paid()
    {
        await using var api = WithFakeSpeech(app, Rate);
        using var member = await SignedInToNewOrganizationAsync(app, api);
        var locked = await SpokenVariantAsync(member);
        var first = await GeneratedAsync(member, locked);
        // The Hook's Scene lasts 3 seconds. Thirteen words are estimated at 3.25, which speeding up by a tenth makes room for;
        // twenty are estimated at 5, which it does not.
        var thirteen = await StoryboardEditTests.EditedAsync(
            member, locked, 1,
            StoryboardEditTests.Changing(first, new SceneEditRequest(1, NarrationText: string.Join(" ", Enumerable.Repeat("ba", 13)))));
        var twenty = await StoryboardEditTests.EditedAsync(
            member, locked, 1,
            StoryboardEditTests.Changing(first, new SceneEditRequest(1, NarrationText: string.Join(" ", Enumerable.Repeat("hai", 20)))));

        var fits = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, thirteen.Version));
        var tooLong = await member.GetAsync<GenerationEstimateResponse>(Generation(locked, twenty.Version));

        Assert.Equal([3250, 1000, 1500, 2800], fits.Speech.Select(speech => speech.EstimatedDurationMs));
        Assert.All(fits.Speech, speech => Assert.Null(speech.Warning));
        Assert.Equal(5000, tooLong.Speech[0].EstimatedDurationMs);
        Assert.NotNull(tooLong.Speech[0].Warning);
        Assert.Contains("Scene 1", tooLong.Speech[0].Warning);
        Assert.Contains("5 seconds", tooLong.Speech[0].Warning);
        Assert.Contains("3 seconds", tooLong.Speech[0].Warning);
        Assert.All(tooLong.Speech.Skip(1), speech => Assert.Null(speech.Warning));
        // A warning is not a refusal: nothing has been paid, and the member may still confirm.
        Assert.True(tooLong.Available);
        Assert.Equal(0m, tooLong.Spent);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A setting of a worker that has the fake speech provider.</summary>
    internal static readonly (string Name, string Value) FakeSpeechWorker = ("Generation__SpeechProvider", "Fake");

    /// <summary>The speech that confirming the estimate of this Storyboard version queues.</summary>
    internal static async Task<IReadOnlyList<SpeechResponse>> ConfirmedSpeechAsync(Browser member, VariantResponse variant, int version)
    {
        var estimate = await member.GetAsync<GenerationEstimateResponse>(Generation(variant, version));
        var response = await member.PostAsync(Generation(variant, version), new ConfirmGenerationRequest(estimate.Total), NewKey());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConfirmedGenerationResponse>(AffiVideoApp.Json, Cancellation))!.Speech;
    }

    /// <summary>The speech once no worker has anything left to do with it: made, or failed.</summary>
    internal static async Task<SpeechResponse> EndedAsync(Browser member, SpeechResponse speech, IContainer worker)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        while (true)
        {
            speech = await member.GetAsync<SpeechResponse>($"/api/v1/speech/{speech.Id}");
            if (speech.State is not (SpeechState.Queued or SpeechState.Generating)) return speech;
            Assert.True(DateTimeOffset.UtcNow < until, $"The speech was still {speech.State} after two minutes.\n{await RenderStack.LogAsync(worker)}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancellation);
        }
    }

    /// <summary>A Product Lock Variant of a Product that has what a Storyboard needs: a photo and a Confirmed Fact.</summary>
    internal static async Task<VariantResponse> SpokenVariantAsync(Browser member)
    {
        var product = await NewProductAsync(member);
        await UploadPhotoAsync(member, product);
        await ConfirmedFactAsync(member, product, "Pin dùng liên tục 30 giờ");
        var project = await ProjectTests.CreateAsync(member, ProjectTests.Valid(product));
        return await VariantTests.AddAsync(member, project.Id);
    }

    /// <summary>Settings of an API that has the fake speech provider, with a character spoken by it taken to cost this much.</summary>
    internal static Dictionary<string, string> FakeSpeech(decimal ratePerCharacter) => new()
    {
        ["Generation:SpeechProvider"] = "Fake",
        ["ProductionCost:FakeSpeechPerCharacter"] = ratePerCharacter.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>The API with the fake speech provider, and a character spoken by it taken to cost this much.</summary>
    internal static WebApplicationFactory<Program> WithFakeSpeech(AffiVideoApp app, decimal ratePerCharacter) => app.With(FakeSpeech(ratePerCharacter));
}
