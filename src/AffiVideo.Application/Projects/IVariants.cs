using AffiVideo.Domain;

namespace AffiVideo.Application.Projects;

/// <summary>
/// The Variants of the caller's Organization's Projects. A Project or a Variant of
/// another Organization is answered exactly as one that does not exist. Nothing
/// here changes a Variant once it is added.
/// </summary>
public interface IVariants
{
    /// <returns>Null when there is no such Project.</returns>
    Task<Variant?> AddAsync(Guid projectId, CreativeTemplate creativeTemplate, string hook, CancellationToken cancellationToken);

    /// <summary>Oldest first. Null when there is no such Project.</summary>
    Task<Page<Variant>?> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Null when the Project has no such Variant.</summary>
    Task<Variant?> FindAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken);

    /// <summary>Adds a separate Variant with the same creative template and another Hook.</summary>
    /// <returns>The new Variant. Null when the Project has no such Variant.</returns>
    Task<VariantDuplication?> DuplicateAsync(Guid projectId, Guid variantId, string hook, CancellationToken cancellationToken);
}

/// <summary>Either the new Variant, or the reason the Hook was refused, in words for the member.</summary>
public sealed record VariantDuplication(Variant? Variant, string? Refused)
{
    public static VariantDuplication Made(Variant variant) => new(variant, null);

    public static VariantDuplication Refuse(string reason) => new(null, reason);
}
