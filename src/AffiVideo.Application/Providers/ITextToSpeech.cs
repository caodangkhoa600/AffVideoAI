using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// Speaks a Scene's narration text. Only the worker asks, and only for speech a
/// member has confirmed: every call may cost money.
/// </summary>
public interface ITextToSpeech
{
    /// <summary>How speech made by this implementation is labelled.</summary>
    SpeechProvider Provider { get; }

    /// <param name="text">What is to be said, as data for the provider.</param>
    /// <param name="language">The language of the text, as an ISO 639-1 code.</param>
    /// <param name="voice">The voice to say it in, by its id in the list of voices on offer.</param>
    /// <returns>The speech, or why there is none. A provider that fails is an answer, not an exception.</returns>
    Task<SpeechGeneration> SpeakAsync(string text, string language, string voice, CancellationToken cancellationToken);
}

/// <summary>How asking for speech ended.</summary>
public abstract record SpeechGeneration
{
    private SpeechGeneration()
    {
    }

    /// <param name="Wav">The speech, as 16-bit PCM in a WAV file, whatever the provider sent.</param>
    /// <param name="DurationMs">How long it lasts, in milliseconds.</param>
    /// <param name="Model">The provider's model that said it.</param>
    public sealed record Made(byte[] Wav, int DurationMs, string Model) : SpeechGeneration;

    /// <summary>The provider would not say this text, and asking again for the same would not help.</summary>
    /// <param name="Reason">Why, as the provider put it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same.</param>
    public sealed record Refused(string Reason, bool Billed) : SpeechGeneration;

    /// <summary>Something went wrong at the provider or on the way to it.</summary>
    /// <param name="Detail">As the provider or the program reported it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same. Only one it did not charge for is tried again.</param>
    public sealed record Failed(string Detail, bool Billed) : SpeechGeneration;
}
