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

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Variant> Variants => Set<Variant>();

    public DbSet<VariantAudio> VariantAudio => Set<VariantAudio>();

    public DbSet<Storyboard> Storyboards => Set<Storyboard>();

    public DbSet<RenderJob> RenderJobs => Set<RenderJob>();

    public DbSet<RenderedVideo> RenderedVideos => Set<RenderedVideo>();

    public DbSet<ClearedFlag> ClearedFlags => Set<ClearedFlag>();

    public DbSet<ProductionCostRecord> ProductionCostRecords => Set<ProductionCostRecord>();

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignVariant> CampaignVariants => Set<CampaignVariant>();

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
            product.Property(p => p.ResearchNotes).HasMaxLength(Product.ResearchNotesMaxLength);
            product.Property(p => p.CommissionRatePercent).HasPrecision(Product.CommissionRatePrecision, Product.CommissionRateDecimals);
            product.Property(p => p.CommissionAmount).HasPrecision(Product.PricePrecision, Product.PriceDecimals);
            product.Property(p => p.CommissionCurrency).HasMaxLength(Product.CurrencyLength);
            product.HasIndex(p => new { p.OrganizationId, p.Name });
        });

        builder.Entity<ProductAsset>(asset =>
        {
            asset.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            asset.HasOne<Product>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
            asset.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            asset.Ignore(a => a.StorageKey);
            asset.Ignore(a => a.VideoLayerKey);
            asset.Ignore(a => a.VideoLayerDetailsKey);
            asset.Ignore(a => a.IsUsableInVideo);
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

        builder.Entity<Project>(project =>
        {
            project.HasOne<Organization>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            project.HasOne<Product>().WithMany().HasForeignKey(p => p.ProductId).OnDelete(DeleteBehavior.Restrict);
            project.Property(p => p.Audience).HasMaxLength(Project.AudienceMaxLength);
            project.Property(p => p.Language).HasMaxLength(ContentLanguages.CodeMaxLength);
            project.Property(p => p.Objective).HasMaxLength(Project.ObjectiveMaxLength);
            project.HasIndex(p => new { p.OrganizationId, p.CreatedAt });
            project.HasIndex(p => new { p.ProductId, p.CreatedAt });
        });

        builder.Entity<Variant>(variant =>
        {
            variant.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // Deleting a Project deletes its Variants with it.
            variant.HasOne<Project>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Cascade);
            variant.Property(v => v.CreativeTemplate).HasConversion<string>().HasMaxLength(30);
            variant.Property(v => v.Hook).HasMaxLength(Variant.HookMaxLength);
            variant.HasIndex(v => new { v.ProjectId, v.CreatedAt });
        });

        builder.Entity<VariantAudio>(audio =>
        {
            audio.ToTable("VariantAudio");
            audio.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // Deleting a Project deletes its Variants, and their audio with them.
            audio.HasOne<Variant>().WithMany().HasForeignKey(a => a.VariantId).OnDelete(DeleteBehavior.Cascade);
            audio.HasOne<Member>().WithMany().HasForeignKey(a => a.RightsConfirmedByMemberId).OnDelete(DeleteBehavior.Restrict);
            audio.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
            audio.Ignore(a => a.StorageKey);
            // A Variant has at most one narration and one music, even when two are uploaded at the same moment.
            audio.HasIndex(a => new { a.VariantId, a.Kind }).IsUnique();
        });

        builder.Entity<Storyboard>(storyboard =>
        {
            storyboard.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // Deleting a Project deletes its Variants, and their Storyboards with them.
            storyboard.HasOne<Variant>().WithMany().HasForeignKey(s => s.VariantId).OnDelete(DeleteBehavior.Cascade);
            storyboard.Property(s => s.CreativeTemplate).HasConversion<string>().HasMaxLength(30);
            storyboard.Property(s => s.Planner).HasConversion<string>().HasMaxLength(30);
            storyboard.Property(s => s.RenderMode).HasConversion<string>().HasMaxLength(20);
            // Two Storyboards generated at the same moment cannot both be the same version.
            storyboard.HasIndex(s => new { s.VariantId, s.Version }).IsUnique();

            // Scenes and the Facts they used are part of the version: read and written with it, never on their own.
            storyboard.OwnsMany(s => s.Scenes, scene =>
            {
                scene.ToTable("StoryboardScenes");
                scene.WithOwner().HasForeignKey("StoryboardId");
                scene.HasKey("StoryboardId", nameof(Scene.Position));
                scene.Property(s => s.Position).ValueGeneratedNever();
                scene.Property(s => s.Layout).HasConversion<string>().HasMaxLength(30);
                scene.Property(s => s.Technique).HasConversion<string>().HasMaxLength(20);

                scene.OwnsMany(s => s.Facts, fact =>
                {
                    fact.ToTable("StoryboardSceneFacts");
                    fact.WithOwner().HasForeignKey("StoryboardId", "ScenePosition");
                    fact.HasKey("StoryboardId", "ScenePosition", nameof(SceneFact.Position));
                    fact.Property(f => f.Position).ValueGeneratedNever();
                    fact.Property(f => f.Text).HasMaxLength(Fact.TextMaxLength);
                    fact.HasOne<Fact>().WithMany().HasForeignKey(f => f.FactId).OnDelete(DeleteBehavior.Restrict);
                    // Every Storyboard that used a Fact is found from the Fact.
                    fact.HasIndex(f => f.FactId);
                });
            });
        });

        builder.Entity<RenderJob>(job =>
        {
            job.HasOne<Organization>().WithMany().HasForeignKey(j => j.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // A job goes with its Storyboard when a Project is deleted. One that made a Rendered Video is held by it, below.
            job.HasOne<Storyboard>().WithMany().HasForeignKey(j => j.StoryboardId).OnDelete(DeleteBehavior.Cascade);
            // A job is only written by whoever still has it as it was read: a worker that
            // has lost its lease, or whose job a member has cancelled, saves nothing.
            job.Property(j => j.State).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            job.Property(j => j.LeaseId).IsConcurrencyToken();
            job.Ignore(j => j.RetryAt);
            job.Property(j => j.IdempotencyKey).HasMaxLength(RenderJob.IdempotencyKeyMaxLength);
            job.Property(j => j.FailureStage).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.FailureCategory).HasConversion<string>().HasMaxLength(20);
            job.Property(j => j.FailureMessage).HasMaxLength(RenderJob.FailureMessageMaxLength);
            job.Property(j => j.FailureDetail).HasMaxLength(RenderJob.FailureDetailMaxLength);
            // One job for one click, even when the click arrives twice at the same moment.
            job.HasIndex(j => new { j.StoryboardId, j.IdempotencyKey }).IsUnique();
            // The queue: the worker takes the job that has been queued longest.
            job.HasIndex(j => new { j.State, j.CreatedAt });
            job.HasIndex(j => new { j.StoryboardId, j.CreatedAt });
        });

        builder.Entity<RenderedVideo>(video =>
        {
            video.HasOne<Organization>().WithMany().HasForeignKey(v => v.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // A Rendered Video is kept: its Storyboard, and so its Variant and Project, cannot be deleted from under it.
            video.HasOne<Storyboard>().WithMany().HasForeignKey(v => v.StoryboardId).OnDelete(DeleteBehavior.Restrict);
            video.HasOne<RenderJob>().WithMany().HasForeignKey(v => v.RenderJobId).OnDelete(DeleteBehavior.Restrict);
            video.HasOne<Member>().WithMany().HasForeignKey(v => v.ApprovedByMemberId).OnDelete(DeleteBehavior.Restrict);
            // An approval is only saved if the state is still the one that was read,
            // so two approvals made at the same moment cannot both be recorded.
            video.Property(v => v.State).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
            video.Ignore(v => v.StorageKey);
            video.Ignore(v => v.CanBeDownloaded);
            video.HasIndex(v => new { v.StoryboardId, v.CreatedAt });
            // The library: an Organization's Rendered Videos by date.
            video.HasIndex(v => new { v.OrganizationId, v.CreatedAt });
            // A job makes one Rendered Video.
            video.HasIndex(v => v.RenderJobId).IsUnique();
        });

        builder.Entity<ClearedFlag>(cleared =>
        {
            cleared.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            cleared.HasOne<Fact>().WithMany().HasForeignKey(c => c.FactId).OnDelete(DeleteBehavior.Restrict);
            cleared.HasOne<Member>().WithMany().HasForeignKey(c => c.ClearedByMemberId).OnDelete(DeleteBehavior.Restrict);
            // A clearing says something about the work it was made on, and goes with it.
            cleared.HasOne<Storyboard>().WithMany().HasForeignKey(c => c.StoryboardId).OnDelete(DeleteBehavior.Cascade);
            cleared.HasOne<RenderedVideo>().WithMany().HasForeignKey(c => c.RenderedVideoId).OnDelete(DeleteBehavior.Cascade);
            // A flag is cleared once, even when two members clear it at the same moment.
            cleared.HasIndex(c => new { c.StoryboardId, c.FactId }).IsUnique();
            cleared.HasIndex(c => new { c.RenderedVideoId, c.FactId }).IsUnique();
            cleared.ToTable(table => table.HasCheckConstraint(
                "CK_ClearedFlags_OneSubject",
                $"(\"{nameof(ClearedFlag.StoryboardId)}\" IS NULL) <> (\"{nameof(ClearedFlag.RenderedVideoId)}\" IS NULL)"));
        });

        builder.Entity<ProductionCostRecord>(cost =>
        {
            cost.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            // What a Product has cost to render is kept with the Product, which is never deleted.
            cost.HasOne<Product>().WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
            // The record outlives its job: a job goes with its Project, and what rendering it cost is still part of the Product's production cost.
            cost.HasOne<RenderJob>().WithMany().HasForeignKey(c => c.RenderJobId).OnDelete(DeleteBehavior.SetNull);
            cost.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(20);
            cost.Property(c => c.Provider).HasConversion<string>().HasMaxLength(20);
            cost.Property(c => c.TechniqueCounts)
                .HasColumnType("jsonb")
                .HasConversion(
                    counts => TechniqueCountsJson.Write(counts),
                    json => TechniqueCountsJson.Read(json),
                    new ValueComparer<IReadOnlyList<TechniqueCount>>(
                        (a, b) => a!.SequenceEqual(b!),
                        counts => counts.Aggregate(0, (hash, count) => HashCode.Combine(hash, count)),
                        counts => counts.ToList()));
            cost.Property(c => c.EstimatedAmount).HasPrecision(ProductionCosts.AmountPrecision, ProductionCosts.AmountDecimals);
            cost.Property(c => c.Currency).HasMaxLength(Product.CurrencyLength);
            cost.Property(c => c.RatesVersion).HasMaxLength(RenderRates.VersionMaxLength);
            // An attempt has one record, whoever comes to write it.
            cost.HasIndex(c => new { c.RenderJobId, c.Attempt }).IsUnique();
            cost.HasIndex(c => c.ProductId);
        });

        builder.Entity<Campaign>(campaign =>
        {
            campaign.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            campaign.Property(c => c.Name).HasMaxLength(Campaign.NameMaxLength);
            campaign.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            campaign.HasIndex(c => new { c.OrganizationId, c.CreatedAt });
        });

        builder.Entity<CampaignVariant>(grouped =>
        {
            // A Campaign groups a Variant once, even when it is added twice at the same moment.
            grouped.HasKey(g => new { g.CampaignId, g.VariantId });
            grouped.HasOne<Organization>().WithMany().HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            grouped.HasOne<Campaign>().WithMany().HasForeignKey(g => g.CampaignId).OnDelete(DeleteBehavior.Cascade);
            // The Campaign does not own the Variant: when a Variant goes with its Project, only its place in the Campaign goes with it.
            grouped.HasOne<Variant>().WithMany().HasForeignKey(g => g.VariantId).OnDelete(DeleteBehavior.Cascade);
            grouped.HasIndex(g => g.VariantId);
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
