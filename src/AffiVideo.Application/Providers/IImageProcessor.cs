namespace AffiVideo.Application.Providers;

/// <summary>
/// Prepares a Product's photo for a video. The implementation is a model that
/// runs locally in the worker; the API has none.
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
