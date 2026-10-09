using AffiVideo.Domain;

namespace AffiVideo.Application.Storyboards;

/// <summary>
/// The Storyboard versions of the caller's Organization's Variants. A Project or a
/// Variant of another Organization is answered exactly as one that does not
/// exist. Nothing here changes a version once it is made.
/// </summary>
public interface IStoryboards
{
    /// <summary>
    /// Plans the Variant's next Storyboard version in Product Lock from the Product's
    /// Confirmed Facts and photos, with whichever planner is configured.
    /// </summary>
    /// <returns>Null when the Project has no such Variant.</returns>
    Task<StoryboardGeneration?> GenerateAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken);

    /// <summary>Newest version first. Null when the Project has no such Variant.</summary>
    Task<Page<Storyboard>?> ListAsync(Guid projectId, Guid variantId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Null when the Variant has no such version.</summary>
    Task<Storyboard?> FindAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken);
}

/// <summary>Either the new Storyboard version, or the reason none was made, in words for the member.</summary>
public sealed record StoryboardGeneration(Storyboard? Storyboard, string? Refused)
{
    public static StoryboardGeneration Made(Storyboard storyboard) => new(storyboard, null);

    public static StoryboardGeneration Refuse(string reason) => new(null, reason);
}
