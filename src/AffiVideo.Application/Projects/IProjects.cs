using AffiVideo.Domain;

namespace AffiVideo.Application.Projects;

/// <summary>
/// The Projects of the caller's Organization. A Project or a Product of another
/// Organization is answered exactly as one that does not exist.
/// </summary>
public interface IProjects
{
    /// <returns>Null when there is no such Product.</returns>
    Task<ProjectRecord?> CreateAsync(Guid productId, ProjectBrief brief, CancellationToken cancellationToken);

    Task<ProjectRecord?> FindAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Newest first, of every Product unless one is asked for.</summary>
    Task<Page<ProjectRecord>> ListAsync(Guid? productId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Deletes the Project and its Variants, and records it in the audit log.</summary>
    /// <returns>False when there is no such Project.</returns>
    Task<bool> DeleteAsync(Guid projectId, CancellationToken cancellationToken);
}

/// <param name="ProductName">As the Product is named now.</param>
public sealed record ProjectRecord(Project Project, string ProductName, int VariantCount);
