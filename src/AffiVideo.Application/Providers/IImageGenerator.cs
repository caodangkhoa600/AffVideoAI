using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// Makes a Still from a description. Only the worker asks, and only for a Still a
/// member has confirmed: every call may cost money.
/// </summary>
public interface IImageGenerator
{
    /// <summary>How Stills made by this implementation are labelled.</summary>
    ImageProvider Provider { get; }

    /// <param name="description">What the picture is to show, as data for the provider.</param>
    /// <returns>The picture, or why there is none. A provider that fails is an answer, not an exception.</returns>
    Task<ImageGeneration> GenerateAsync(string description, CancellationToken cancellationToken);
}

/// <summary>How asking for a picture ended.</summary>
public abstract record ImageGeneration
{
    private ImageGeneration()
    {
    }

    /// <param name="Png">The picture, vertical, as a PNG file.</param>
    /// <param name="Model">The provider's model that made it.</param>
    public sealed record Made(byte[] Png, string Model) : ImageGeneration;

    /// <summary>The provider would not make this picture, and asking again for the same would not help.</summary>
    /// <param name="Reason">Why, as the provider put it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same.</param>
    public sealed record Refused(string Reason, bool Billed) : ImageGeneration;

    /// <summary>Something went wrong at the provider or on the way to it.</summary>
    /// <param name="Detail">As the provider or the program reported it.</param>
    /// <param name="Billed">Whether the provider charged for the attempt all the same. Only one it did not charge for is tried again.</param>
    public sealed record Failed(string Detail, bool Billed) : ImageGeneration;
}
