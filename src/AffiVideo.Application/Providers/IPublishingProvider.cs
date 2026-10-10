using AffiVideo.Domain;

namespace AffiVideo.Application.Providers;

/// <summary>
/// Posts an approved Rendered Video on a social account. It has no implementation:
/// publishing is done by hand, and a Published Post is what a member records afterwards.
/// </summary>
public interface IPublishingProvider
{
    /// <summary>The platform this implementation posts on.</summary>
    SocialPlatform Platform { get; }

    /// <param name="video">The MP4 of the Rendered Video.</param>
    /// <param name="handle">The social account to post on, as it is called on the platform.</param>
    /// <param name="caption">The text posted with the video, as data for the provider.</param>
    /// <returns>The address of what was posted.</returns>
    Task<Uri> PublishAsync(Stream video, string handle, string caption, CancellationToken cancellationToken);
}
