using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// Makes a Clip from a Still, for generated footage. Only the worker asks, and
/// only for a Clip a member has confirmed, from a Still a member has approved:
/// every call may cost money. It never starts from a Product's photo (ADR 0004).
/// </summary>
public interface IVideoGenerator
{
    /// <summary>How Clips made by this implementation are labelled.</summary>
    VideoProvider Provider { get; }

    /// <param name="still">The approved Still the Clip starts from, as a PNG file.</param>
    /// <param name="description">What the footage is to show, as data for the provider.</param>
    /// <param name="duration">How long the Scene lasts. The video is at least this long.</param>
    /// <returns>The video, or why there is none. A provider that fails is an answer, not an exception.</returns>
    Task<VideoGeneration> GenerateAsync(byte[] still, string description, TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>How asking for a video ended.</summary>
public abstract record VideoGeneration
{
    private VideoGeneration()
    {
    }

    /// <param name="Mp4">The video, vertical, as an MP4 file. Whatever sound the provider put in it is removed before it is kept.</param>
    /// <param name="Model">The provider's model that made it.</param>
    public sealed record Made(byte[] Mp4, string Model) : VideoGeneration;

    /// <summary>The provider would not make this footage, and asking again for the same would not help.</summary>
    /// <param name="Reason">Why, as the provider put it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same.</param>
    public sealed record Refused(string Reason, bool Billed) : VideoGeneration;

    /// <summary>Something went wrong at the provider or on the way to it.</summary>
    /// <param name="Detail">As the provider or the program reported it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same. Only one it did not charge for is tried again.</param>
    public sealed record Failed(string Detail, bool Billed) : VideoGeneration;
}
