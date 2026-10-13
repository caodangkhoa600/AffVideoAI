namespace AffiVideo.Domain;

/// <summary>How fast a language is taken to be spoken, for estimating how long narration text lasts before it is said.</summary>
/// <param name="WordsPerSecond">Words as they are separated by spaces. In Vietnamese each is one syllable.</param>
/// <param name="SentencePauseMs">The pause after a mark that ends a sentence, when more follows.</param>
/// <param name="ClausePauseMs">The pause after a comma, a semicolon or a colon, when more follows.</param>
public sealed record SpeakingPace(double WordsPerSecond, int SentencePauseMs, int ClausePauseMs);

public static class SpeechLength
{
    /// <summary>
    /// How long the text is estimated to take to say, in milliseconds: its words at the pace, and a pause
    /// at each mark that something follows. It is a guess made from the text alone, to warn before
    /// anything is paid for. How long speech lasts is known only once it is made.
    /// </summary>
    public static int EstimateMs(string text, SpeakingPace pace)
    {
        var words = TextLimit.Words(text);
        if (words.Length == 0) return 0;

        // The mark that ends the text is followed by nothing, so nobody waits after it.
        var pauses = words[..^1].Sum(word => word[^1] switch
        {
            '.' or '!' or '?' or '…' => pace.SentencePauseMs,
            ',' or ';' or ':' => pace.ClausePauseMs,
            _ => 0,
        });
        return (int)Math.Round(words.Length * 1000 / pace.WordsPerSecond, MidpointRounding.AwayFromZero) + pauses;
    }
}

public static class SpeechFit
{
    /// <summary>The most speech is sped up to fit its Scene: beyond this it no longer sounds like someone speaking.</summary>
    public const double MaxTempo = 1.1;

    /// <summary>
    /// How much faster speech has to be played to end with its Scene: 1 when it fits as it is.
    /// Null when that would take more than <see cref="MaxTempo"/>, and the speech does not fit.
    /// </summary>
    public static double? TempoFor(int speechMs, int sceneMs)
    {
        if (speechMs <= sceneMs) return 1;
        // Compared in whole milliseconds, so that a tenth more is allowed exactly.
        return sceneMs > 0 && speechMs * 10L <= sceneMs * 11L ? (double)speechMs / sceneMs : null;
    }
}
