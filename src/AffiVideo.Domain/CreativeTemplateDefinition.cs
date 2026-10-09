using System.Globalization;

namespace AffiVideo.Domain;

/// <summary>
/// What a creative template is to the planner: its Scenes in order, how the
/// duration is shared between them, and how much text each layout holds. How a
/// layout looks is the Remotion component of the same name (ADR 0002).
/// </summary>
/// <param name="Version">Goes up whenever the Scenes, shares or limits change, and is recorded on each Storyboard.</param>
/// <param name="MaxFacts">The most Facts the template shows, one at a time.</param>
/// <param name="FactPacing">How the Facts layout spends its Scene's time.</param>
/// <param name="NameLimit">The limit on the Product's name, which the template sets in type.</param>
public sealed record CreativeTemplateDefinition(
    CreativeTemplate Template,
    int Version,
    IReadOnlyList<SceneSlot> Scenes,
    int MaxFacts,
    FactPacing FactPacing,
    TextLimit HookLimit,
    TextLimit FactLimit,
    TextLimit NameLimit)
{
    /// <summary>The duration of each Scene in milliseconds, in order, summing exactly to the target.</summary>
    public IReadOnlyList<int> SceneDurations(int targetDurationSeconds) =>
        Domain.SceneDurations.Allocate(targetDurationSeconds, Scenes.Select(scene => scene.Share).ToArray());

    /// <summary>
    /// The most words a Fact can have when this many Facts share the Facts Scene of a
    /// video of this duration: every word has arrived and been held on screen before
    /// the Fact leaves. Zero when the Facts are given too little time for even one word.
    /// </summary>
    public int MaxFactWords(int targetDurationSeconds, int factCount)
    {
        var durations = SceneDurations(targetDurationSeconds);
        var scene = Scenes.Select((slot, index) => (slot, index)).First(found => found.slot.Layout == SceneLayout.Facts).index;
        var forWords = (durations[scene] - FactPacing.LeadInMs) / factCount - FactPacing.AroundEachFactMs;
        return forWords < 0 ? 0 : forWords / FactPacing.WordStepMs + 1;
    }

    /// <summary>
    /// How many of these Facts, oldest first, the template shows in a video of this
    /// duration: as many as it has room for, and fewer when that is what it takes for
    /// each to be read in its time. Zero when the first Fact alone cannot be.
    /// </summary>
    public int FactsShown(IReadOnlyList<string> oldestFirst, int targetDurationSeconds)
    {
        for (var count = Math.Min(MaxFacts, oldestFirst.Count); count > 0; count--)
        {
            var most = MaxFactWords(targetDurationSeconds, count);
            if (oldestFirst.Take(count).All(fact => TextLimit.Words(fact).Length <= most)) return count;
        }
        return 0;
    }
}

/// <summary>How the Facts layout spends its Scene's time, in milliseconds.</summary>
/// <param name="LeadInMs">Before the first Fact, while the layout arrives.</param>
/// <param name="AroundEachFactMs">Of each Fact's time, what is not spent on words arriving: the first word settling, the hold once all are in, and the fade out.</param>
/// <param name="WordStepMs">Between one word starting to arrive and the next.</param>
public sealed record FactPacing(int LeadInMs, int AroundEachFactMs, int WordStepMs);

/// <summary>One Scene of a creative template.</summary>
/// <param name="Layout">Which layout of the template draws the Scene.</param>
/// <param name="Share">The Scene's part of the duration, relative to the other Scenes' shares.</param>
/// <param name="Techniques">The Techniques the Scene can be produced with, the one it would rather have first.</param>
/// <param name="Photo">Which of the Product's photos the Scene shows.</param>
public sealed record SceneSlot(
    SceneLayout Layout,
    int Share,
    IReadOnlyList<Technique> Techniques,
    ScenePhoto Photo = ScenePhoto.First);

/// <summary>
/// A layout of a creative template. Each fixes what the lines of a Scene's
/// on-screen text are.
/// </summary>
public enum SceneLayout
{
    /// <summary>The Hook in type that fills the frame around the Product. One line: the Hook.</summary>
    Hook,

    /// <summary>The Product large, with its name. One line: the Product's name.</summary>
    Reveal,

    /// <summary>Facts shown one at a time beside the Product. One line for each Fact.</summary>
    Facts,

    /// <summary>The Product, its name and the call to action. Two lines: the name, then the call to action.</summary>
    Closing,
}

public enum ScenePhoto
{
    /// <summary>The Product's oldest usable photo.</summary>
    First,

    /// <summary>Its newest, so that a Product with several photos is seen from more than one side.</summary>
    Last,
}

/// <summary>How much text a layout holds at a size that can be read on a phone.</summary>
/// <param name="MaxCharacters">Of the whole text.</param>
/// <param name="MaxWordCharacters">Of one word, which is never broken across lines.</param>
/// <param name="MaxWords">Set where the words arrive one by one and all have to be in place by a given moment.</param>
public sealed record TextLimit(int MaxCharacters, int MaxWordCharacters, int? MaxWords = null)
{
    /// <summary>Why the text does not fit, in words for the member. Null when it fits.</summary>
    /// <param name="subject">How the member is told which text it is, to open a sentence: "The Hook".</param>
    public string? Exceeded(string subject, string text)
    {
        var words = Words(text);
        if (MaxWords is { } most && words.Length > most)
        {
            return $"{subject} has {words.Length} words, and at most {most} can be fully on screen within the first two seconds.";
        }

        var characters = Length(text);
        if (characters > MaxCharacters)
        {
            return $"{subject} is too long for its layout: it has {characters} characters, and at most {MaxCharacters} fit.";
        }

        return words.Any(word => Length(word) > MaxWordCharacters)
            ? $"{subject} has a word too long for its layout: at most {MaxWordCharacters} characters fit on one line."
            : null;
    }

    /// <summary>The words of a text, as the layouts bring them in one by one.</summary>
    public static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // As a reader counts them: a letter with its diacritics is one, however it is encoded.
    private static int Length(string text) => new StringInfo(text).LengthInTextElements;
}

public static class CreativeTemplates
{
    /// <summary>
    /// The look the founder approved in ticket 26: the Hook, the reveal, the Facts
    /// and the closing, as 3, 5, 7 and 5 seconds of a 20-second video.
    /// </summary>
    private static readonly CreativeTemplateDefinition ProductShowcase = new(
        CreativeTemplate.ProductShowcase,
        Version: 1,
        Scenes:
        [
            new SceneSlot(SceneLayout.Hook, Share: 3, [Technique.ImageMotion, Technique.StaticImage]),
            new SceneSlot(SceneLayout.Reveal, Share: 5, [Technique.ImageToVideo, Technique.ImageMotion, Technique.StaticImage]),
            new SceneSlot(SceneLayout.Facts, Share: 7, [Technique.TextAnimation], ScenePhoto.Last),
            new SceneSlot(SceneLayout.Closing, Share: 5, [Technique.ImageMotion, Technique.StaticImage]),
        ],
        MaxFacts: 3,
        // The Facts panel takes 0.7 seconds to arrive. Within a Fact's time its first word starts 0.1
        // seconds in and takes 0.4 to settle, each further word follows 0.06 seconds later, and the Fact
        // fades over its last 0.2 seconds. Half a second is the least it is held complete before that.
        FactPacing: new FactPacing(LeadInMs: 700, AroundEachFactMs: 100 + 400 + 500 + 200, WordStepMs: 60),
        // The Hook's words arrive 0.12 seconds apart from 0.08 seconds in and each takes
        // 0.4 seconds to settle, so the thirteenth is in place at 1.92 seconds and a
        // fourteenth would not be until 2.04.
        HookLimit: new TextLimit(MaxCharacters: 60, MaxWordCharacters: 12, MaxWords: 13),
        FactLimit: new TextLimit(MaxCharacters: 120, MaxWordCharacters: 24),
        NameLimit: new TextLimit(MaxCharacters: 60, MaxWordCharacters: 24));

    /// <summary>Null for a creative template that cannot be planned yet.</summary>
    public static CreativeTemplateDefinition? Find(CreativeTemplate template) =>
        template == CreativeTemplate.ProductShowcase ? ProductShowcase : null;
}
