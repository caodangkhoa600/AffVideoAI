using AffiVideo.Domain;

namespace AffiVideo.Application.Lab;

/// <summary>
/// What the Affiliate Lab keeps about the Products of the caller's Organization.
/// They are the Organization's own Products, not a list of the Lab's. A Product of
/// another Organization is answered with null, exactly as one that does not exist.
/// </summary>
public interface ILabProducts
{
    /// <summary>By name.</summary>
    /// <param name="shortlisted">True for the shortlist only, false for the rest only.</param>
    Task<Page<Product>> ListAsync(bool? shortlisted, PageRequest page, CancellationToken cancellationToken);

    Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken);

    Task<Product?> ChangeAsync(Guid productId, LabProductDetails details, CancellationToken cancellationToken);
}
