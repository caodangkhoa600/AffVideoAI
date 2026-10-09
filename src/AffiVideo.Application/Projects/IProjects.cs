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

    /// <summary>
    /// Deletes the Project with its Variants and their Storyboards, and records it in
    /// the audit log. A Project that has a Rendered Video is kept.
    /// </summary>
    Task<ProjectDeletion> DeleteAsync(Guid projectId, CancellationToken cancellationToken);
}

public enum ProjectDeletion
{
    Deleted,

    /// <summary>There is no such Project.</summary>
    NotFound,

    /// <summary>Nothing was deleted: a Rendered Video was made from one of the Project's Storyboards.</summary>
    HasRenderedVideos,
}

/// <param name="ProductName">As the Product is named now.</param>
public sealed record ProjectRecord(Project Project, string ProductName, int VariantCount);
