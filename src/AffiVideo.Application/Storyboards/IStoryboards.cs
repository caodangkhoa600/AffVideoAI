using AffiVideo.Domain;

namespace AffiVideo.Application.Storyboards;

/// <summary>
/// The Storyboard versions of the caller's Organization's Variants. A Project or a
/// Variant of another Organization is answered exactly as one that does not
/// exist. Nothing here changes a version once it is made: an edit makes the next.
/// </summary>
public interface IStoryboards
{
    /// <summary>
    /// Plans the Variant's next Storyboard version in Product Lock from the Product's
    /// Confirmed Facts and photos, with whichever planner is configured.
    /// </summary>
    /// <returns>Null when the Project has no such Variant.</returns>
    Task<StoryboardGeneration?> GenerateAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the Variant's next Storyboard version from this one with these changes
    /// made: text, the photo a Scene shows, the order and the durations. A Scene
    /// whose text changes is marked Manually Edited.
    /// </summary>
    /// <param name="changes">Every Scene of the version, once, in the order the next version plays them.</param>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<StoryboardGeneration?> EditAsync(
        Guid projectId, Guid variantId, int version, IReadOnlyList<SceneChange> changes, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the Variant's next Storyboard version from this one with one Scene
    /// planned again from the Product's Confirmed Facts and photos as they are now.
    /// The Scene keeps its place and its duration, and every other Scene is as it was.
    /// </summary>
    /// <returns>Null when the Variant has no such version, or the version no such Scene.</returns>
    Task<StoryboardGeneration?> RegenerateSceneAsync(
        Guid projectId, Guid variantId, int version, int position, CancellationToken cancellationToken);

    /// <summary>Newest version first. Null when the Project has no such Variant.</summary>
    Task<Page<StoryboardRecord>?> ListAsync(Guid projectId, Guid variantId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Null when the Variant has no such version.</summary>
    Task<StoryboardRecord?> FindAsync(Guid projectId, Guid variantId, int version, CancellationToken cancellationToken);

    /// <summary>
    /// Clears the flags these Withdrawn Facts put on a version, in the calling
    /// member's name, and records it in the audit log. The version itself, any
    /// other flag on it, and any Rendered Video made from it, stay as they are.
    /// </summary>
    /// <param name="factIds">The Facts whose flags the member has reviewed.</param>
    /// <returns>Null when the Variant has no such version.</returns>
    Task<StoryboardFlagClearing?> ClearFlagAsync(
        Guid projectId, Guid variantId, int version, IReadOnlyCollection<Guid> factIds, CancellationToken cancellationToken);
}

/// <summary>A Storyboard version with what has happened around it since it was made.</summary>
/// <param name="Flags">One for each Withdrawn Fact the version used, until a member clears it. Empty when it is not Flagged for Review.</param>
public sealed record StoryboardRecord(Storyboard Storyboard, IReadOnlyList<ReviewFlag> Flags);

/// <summary>Either the Storyboard version as it now is, or the reason no flag was cleared, in words for the member.</summary>
public sealed record StoryboardFlagClearing(StoryboardRecord? Record, string? Refused)
{
    public static StoryboardFlagClearing Made(StoryboardRecord record) => new(record, null);

    public static StoryboardFlagClearing Refuse(string reason) => new(null, reason);
}

/// <summary>Either the new Storyboard version, or the reason none was made, in words for the member.</summary>
public sealed record StoryboardGeneration(Storyboard? Storyboard, string? Refused)
{
    public static StoryboardGeneration Made(Storyboard storyboard) => new(storyboard, null);

    public static StoryboardGeneration Refuse(string reason) => new(null, reason);
}
