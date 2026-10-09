using System.Reflection;
using AffiVideo.Application;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AffiVideo.Infrastructure.Persistence;

/// <summary>
/// Organizations are kept apart here. Every query for an Organization or for a record
/// that is <see cref="IOwnedByOrganization"/> is filtered to the caller's Organization,
/// and saving refuses any such record that belongs to another one. A new
/// entity an Organization owns only has to implement <see cref="IOwnedByOrganization"/>.
/// </summary>
public sealed class AffiVideoDbContext(DbContextOptions<AffiVideoDbContext> options, Caller caller)
    : IdentityUserContext<Member, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAsset> ProductAssets => Set<ProductAsset>();

    public DbSet<Fact> Facts => Set<Fact>();

    /// <summary>The keys that protect session cookies and anti-forgery tokens, kept here so sessions outlive a restart of the API.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // The filters below read this from the context running the query, so one
    // cached model serves every caller.
    private Guid? CallerOrganizationId => caller.OrganizationId;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organization>(organization =>
        {
            organization.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength);
            organization.HasQueryFilter(o => o.Id == CallerOrganizationId);
        });

        builder.Entity<Member>(member =>
        {
            member.ToTable("Members");
            member.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            member.Property(m => m.Role).HasConversion<string>().HasMaxLength(20);
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("MemberClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("MemberLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("MemberTokens");

        builder.Entity<AuditLogEntry>(entry =>
        {
            entry.ToTable("AuditLog");
            entry.HasOne<Organization>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<Member>().WithMany().HasForeignKey(e => e.ActorMemberId).OnDelete(DeleteBehavior.Restrict);
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.HasIndex(e => new { e.OrganizationId, e.OccurredAt });
        });

        builder.Entity<Product>(product =>
        {
            product.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            product.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
            product.Property(p => p.Category).HasMaxLength(Product.CategoryMaxLength);
            product.Property(p => p.Brand).HasMaxLength(Product.BrandMaxLength);
            product.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
            product.Property(p => p.Price).HasPrecision(Product.PricePrecision, Product.PriceDecimals);
            product.Property(p => p.Currency).HasMaxLength(Product.CurrencyLength);
            product.Property(p => p.OriginalUrl).HasMaxLength(Product.UrlMaxLength);
            product.Property(p => p.AffiliateUrl).HasMaxLength(Product.UrlMaxLength);
            product.Property(p => p.TargetAudience).HasMaxLength(Product.TargetAudienceMaxLength);
            product.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            product.HasIndex(p => new { p.OrganizationId, p.Name });
        });

        builder.Entity<ProductAsset>(asset =>
        {
            asset.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
            asset.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            asset.Ignore(a => a.StorageKey);
            asset.HasIndex(a => new { a.ProductId, a.CreatedAt });
            // A Product has at most one logo, even when two are uploaded at the same moment.
            asset.HasIndex(a => a.ProductId, "IX_ProductAssets_OneLogoPerProduct")
                .IsUnique()
                .HasFilter($"\"{nameof(ProductAsset.Kind)}\" = '{nameof(ProductAssetKind.Logo)}'");
        });

        builder.Entity<Fact>(fact =>
        {
            fact.HasOne<Organization>().WithMany().HasForeignKey(f => f.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            fact.HasOne<Product>().WithMany().HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Restrict);
            fact.HasOne<Member>().WithMany().HasForeignKey(f => f.ConfirmedByMemberId).OnDelete(DeleteBehavior.Restrict);
            fact.Property(f => f.Text).HasMaxLength(Fact.TextMaxLength);
            fact.Property(f => f.Language).HasMaxLength(ContentLanguages.CodeMaxLength);
            fact.Property(f => f.Source).HasMaxLength(Fact.SourceMaxLength);
            // A change of state is only saved if the state is still the one that was read,
            // so two changes made at the same moment cannot both be recorded.
            fact.Property(f => f.State).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            fact.HasIndex(f => new { f.ProductId, f.State, f.CreatedAt });
        });

        var filter = typeof(AffiVideoDbContext).GetMethod(nameof(FilterToCallerOrganization), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => typeof(IOwnedByOrganization).IsAssignableFrom(t.ClrType)).ToList())
        {
            filter.MakeGenericMethod(entityType.ClrType).Invoke(this, [builder]);
        }
    }

    private void FilterToCallerOrganization<T>(ModelBuilder builder) where T : class, IOwnedByOrganization =>
        builder.Entity<T>().HasQueryFilter(record => record.OrganizationId == CallerOrganizationId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefuseWritesForAnotherOrganization();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefuseWritesForAnotherOrganization();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The second check, which the filters cannot make: a record built or loaded
    // some other way must still belong to the caller's Organization to be written.
    private void RefuseWritesForAnotherOrganization()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;
            if (OwningOrganization(entry) is { } owner && owner != caller.OrganizationId)
            {
                throw new InvalidOperationException(
                    $"A {entry.Metadata.ClrType.Name} of Organization {owner} cannot be written by a caller acting for " +
                    (caller.OrganizationId is { } own ? $"Organization {own}." : "no Organization."));
            }
        }
    }

    private static Guid? OwningOrganization(EntityEntry entry) => entry.Entity switch
    {
        Organization organization => organization.Id,
        IOwnedByOrganization owned => owned.OrganizationId,
        _ => null,
    };
}
