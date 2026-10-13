using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// What generates pictures and footage for Hybrid, and speech for any video. With nothing configured,
/// nothing is generated: Hybrid plans what Product Lock does, and generation says it is unavailable.
/// </summary>
public sealed class GenerationOptions
{
    public const string Section = "Generation";

    /// <summary>What makes Stills.</summary>
    public ImageProvider ImageProvider { get; set; } = ImageProvider.None;

    /// <summary>What makes Clips from Stills.</summary>
    public VideoProvider VideoProvider { get; set; } = VideoProvider.None;

    /// <summary>What speaks a Scene's narration text.</summary>
    public SpeechProvider SpeechProvider { get; set; } = SpeechProvider.None;

    /// <summary>Whether there is anything to speak narration text with. It takes nothing else: speech is made in Product Lock too.</summary>
    public bool CanSpeak => SpeechProvider != SpeechProvider.None;

    /// <summary>Whether there is anything to make a Still with.</summary>
    public bool CanGenerateStills => ImageProvider != ImageProvider.None;

    /// <summary>Whether there is anything to make a Clip with. A Clip starts from a Still, so that takes both.</summary>
    public bool CanGenerateClips => CanGenerateStills && VideoProvider != VideoProvider.None;

    /// <summary>How the fake image provider misbehaves, for seeing what a member is shown when a real one does.</summary>
    public FakeGenerationOptions Fake { get; set; } = new();

    /// <summary>The same, for the fake video provider.</summary>
    public FakeGenerationOptions FakeVideo { get; set; } = new();

    /// <summary>The same, for the fake speech provider.</summary>
    public FakeGenerationOptions FakeSpeech { get; set; } = new();

    /// <summary>
    /// The voices narration text can be spoken in: a short list, each checked by ear, and not the provider's
    /// whole library. The first is an Organization's until its Owner chooses another.
    /// </summary>
    public List<VoiceOption> Voices { get; set; } = [];

    /// <summary>The voices on offer. With none configured there is the built-in one, so that there is always a voice.</summary>
    public IReadOnlyList<VoiceOption> VoicesOnOffer =>
        Voices.Count > 0 ? Voices : [new VoiceOption { Id = Project.BuiltInVoice, Name = "Sarah" }];

    /// <summary>
    /// How many words a second each language is taken to be spoken at, by its ISO 639-1 code, for estimating how
    /// long narration text lasts before it is said. A Vietnamese word is one syllable, so more of them fit a second.
    /// </summary>
    public Dictionary<string, double> WordsPerSecond { get; set; } = new()
    {
        [ContentLanguages.Vietnamese] = 4,
        [ContentLanguages.English] = 2.5,
    };

    /// <summary>The pace narration text in this language is estimated at. A language with none configured is taken to be as slow as the slowest.</summary>
    public SpeakingPace PaceOf(string language) => new(
        WordsPerSecond.TryGetValue(language, out var pace) && pace > 0 ? pace : 2.5, SentencePauseMs: 300, ClausePauseMs: 150);
}

/// <summary>One of the voices on offer.</summary>
public sealed class VoiceOption
{
    /// <summary>What a Project's brief names it by, and what the speech provider is asked for.</summary>
    public string Id { get; set; } = "";

    /// <summary>What a member is shown.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// A recording of the voice, for a member to play before choosing: an MP3 or WAV file, by its path. A path
    /// that is not absolute is from where the application is. Playing it asks no provider for anything.
    /// </summary>
    public string? Sample { get; set; }
}

/// <summary>How a fake provider misbehaves. With nothing set, it makes what it is asked for every time.</summary>
public sealed class FakeGenerationOptions
{
    /// <summary>How many times the fake fails for a technical reason before it starts making what it is asked for.</summary>
    public int TechnicalFailures { get; set; }

    /// <summary>Whether a failure is one the provider charged for all the same.</summary>
    public bool BillsFailures { get; set; }

    /// <summary>Whether the fake refuses everything, as a provider refuses a picture its rules do not allow.</summary>
    public bool Refuses { get; set; }

    /// <summary>How long the fake takes to answer, as a provider takes a while.</summary>
    public double DelaySeconds { get; set; }
}
