namespace AffiVideo.Application.Providers;

/// <summary>
/// Prepares a Product's photo for a video. It has no implementation yet; the
/// first is the cut-out that runs locally in the worker (ticket 09).
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Cuts the Product out of its photo. Only the photo's transparency is
    /// decided: the Product's own pixels are never repainted.
    /// </summary>
    /// <returns>The same image as a PNG with everything but the Product transparent.</returns>
    Task<Stream> CutOutAsync(Stream photo, CancellationToken cancellationToken);
}
