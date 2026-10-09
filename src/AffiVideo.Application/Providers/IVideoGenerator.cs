namespace AffiVideo.Application.Providers;

/// <summary>
/// Generates a video from an image, for the image-to-video Technique. It has
/// no implementation: nothing generative is chosen until there is one.
/// </summary>
public interface IVideoGenerator
{
    /// <param name="image">The photo to start from.</param>
    /// <param name="direction">How the video should move, as data for the provider.</param>
    /// <returns>The video, as a file.</returns>
    Task<Stream> GenerateAsync(Stream image, string direction, TimeSpan duration, CancellationToken cancellationToken);
}
