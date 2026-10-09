namespace AffiVideo.Application.Providers;

/// <summary>
/// Speaks a Scene's narration text. It has no implementation: a video has the
/// audio a member uploads, or a silent track.
/// </summary>
public interface ITextToSpeech
{
    /// <param name="language">The language of the text, as an ISO 639-1 code.</param>
    /// <returns>The speech, as an audio file.</returns>
    Task<Stream> SpeakAsync(string text, string language, CancellationToken cancellationToken);
}
