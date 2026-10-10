using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Lab;

// No method names an Organization: the context's filter leaves only the caller's Products.
internal sealed class ScopedLabProducts(AffiVideoDbContext database, TimeProvider clock) : ILabProducts
{
    public async Task<Page<Product>> ListAsync(bool? shortlisted, PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.Products.AsNoTracking();
        if (shortlisted is { } only)
        {
            all = all.Where(p => (p.ShortlistedAt != null) == only);
        }

        var items = await all
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<Product>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        database.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

    public async Task<Product?> ChangeAsync(Guid productId, LabProductDetails details, CancellationToken cancellationToken)
    {
        var product = await FindAsync(productId, cancellationToken);
        if (product is null) return null;

        product.ChangeLabDetails(details, clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        return product;
    }
}
