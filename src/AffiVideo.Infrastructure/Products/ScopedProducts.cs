using AffiVideo.Application;
using AffiVideo.Application.Products;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AffiVideo.Infrastructure.Products;

// No method names an Organization: the context's filter leaves only the caller's Products.
internal sealed class ScopedProducts(AffiVideoDbContext database, Caller caller, TimeProvider clock) : IProducts
{
    public async Task<Product> CreateAsync(ProductDetails details, CancellationToken cancellationToken)
    {
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("A Product is created for the caller's Organization, and there is no caller.");

        var product = new Product(Guid.CreateVersion7(), organizationId, details, clock.GetUtcNow());
        database.Products.Add(product);
        await database.SaveChangesAsync(cancellationToken);
        return product;
    }

    public Task<Product?> FindAsync(Guid productId, CancellationToken cancellationToken) =>
        database.Products.SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

    public async Task<Product?> ChangeAsync(Guid productId, ProductDetails details, CancellationToken cancellationToken)
    {
        var product = await FindAsync(productId, cancellationToken);
        if (product is null) return null;

        product.Change(details, clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Product?> ArchiveAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await FindAsync(productId, cancellationToken);
        if (product is null) return null;

        product.Archive(clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Page<Product>> ListAsync(ProductFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var all = database.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var contains = $"%{EscapeLike(filter.Search.Trim())}%";
            all = all.Where(p => EF.Functions.ILike(p.Name, contains, LikeEscape));
        }
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var category = filter.Category.Trim();
            all = all.Where(p => p.Category == category);
        }
        if (filter.Status is { } status)
        {
            all = all.Where(p => p.Status == status);
        }

        var items = await all
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return new Page<Product>(items, page.Page, page.PageSize, await all.CountAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        await database.Products.Select(p => p.Category).Distinct().OrderBy(category => category).ToListAsync(cancellationToken);

    private const string LikeEscape = "\\";

    // What a member searches for is text, never a pattern.
    private static string EscapeLike(string text) => text
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
