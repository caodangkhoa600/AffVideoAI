using AffiVideo.Application;
using AffiVideo.Application.Lab;
using AffiVideo.Application.Organizations;
using AffiVideo.Application.Products;
using AffiVideo.Application.Projects;
using AffiVideo.Application.Providers;
using AffiVideo.Application.Rendering;
using AffiVideo.Application.Storage;
using AffiVideo.Application.Storyboards;
using AffiVideo.Application.SystemStatus;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Identity;
using AffiVideo.Infrastructure.Lab;
using AffiVideo.Infrastructure.Organizations;
using AffiVideo.Infrastructure.Persistence;
using AffiVideo.Infrastructure.Planning;
using AffiVideo.Infrastructure.Products;
using AffiVideo.Infrastructure.Projects;
using AffiVideo.Infrastructure.Rendering;
using AffiVideo.Infrastructure.Storage;
using AffiVideo.Infrastructure.Storyboards;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AffiVideo.Infrastructure;

public static class InfrastructureSetup
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Caller>();
        services.AddDbContext<AffiVideoDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Database")));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton<S3ObjectStorage>();
        services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<S3ObjectStorage>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ISystemStatusReader, SystemStatusReader>();

        services.AddIdentityCore<Member>(options =>
            {
                // A member signs in with their email, so it is their user name too
                // and only has to be a valid, unused email.
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = "";
                // Length is what makes a password hard to guess; composition rules are not.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddUserStore<MemberStore>()
            .AddClaimsPrincipalFactory<MemberClaimsPrincipalFactory>();
        services.AddScoped<IOrganizations, ScopedOrganizations>();
        services.AddScoped<IOrganizationProvisioner, OrganizationProvisioner>();
        services.AddScoped<IProducts, ScopedProducts>();
        services.AddScoped<IProductAssets, ScopedProductAssets>();
        services.AddScoped<IFacts, ScopedFacts>();
        services.AddScoped<IProjects, ScopedProjects>();
        services.AddScoped<IVariants, ScopedVariants>();
        services.AddScoped<IVariantAudio, ScopedVariantAudio>();
        // The only planner there is. A real language model is registered in its place, never beside it.
        services.AddSingleton<ILanguageModel, MockLanguageModel>();
        services.AddScoped<IStoryboards, ScopedStoryboards>();
        services.AddScoped<IRenders, ScopedRenders>();
        services.AddScoped<IRenderedVideos, ScopedRenderedVideos>();
        services.AddScoped<ILabProducts, ScopedLabProducts>();
        services.AddScoped<ICampaigns, ScopedCampaigns>();
        services.AddScoped<DemonstrationSeed>();
        return services;
    }

    /// <summary>
    /// The render queue as the worker sees it, across Organizations. Only the worker
    /// asks for this: nothing in the API can claim a job.
    /// </summary>
    public static IServiceCollection AddRenderQueue(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RenderQueueOptions>(configuration.GetSection(RenderQueueOptions.Section));
        // Rates nobody could estimate with stop the worker as it starts, not at its first render.
        services.AddOptions<ProductionCostOptions>()
            .Bind(configuration.GetSection(ProductionCostOptions.Section))
            .Validate(
                options => Enum.GetValues<RenderProvider>().All(provider => options.RatesOf(provider).Problem is null),
                $"The rates under {ProductionCostOptions.Section} cannot be used: the version is a name, " +
                $"the currency a three-letter code in capitals, and a rate is from zero to {RenderRates.MaxAmount:0}.")
            .ValidateOnStart();
        return services.AddScoped<IRenderQueue, RenderQueue>();
    }

    /// <summary>
    /// Applies every pending database migration and creates the storage bucket if it
    /// is missing. Only the explicit migrate command calls this; nothing changes the
    /// schema at startup.
    /// </summary>
    public static async Task MigrateAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AffiVideoDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<S3ObjectStorage>().EnsureBucketExistsAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the demonstration Organization, its Owner, its sample Product and
    /// that Product's Facts, and gives the Organization the Affiliate Lab, each unless it is already there. Only the explicit seed command calls this.
    /// </summary>
    /// <returns>Whether anything was created.</returns>
    public static async Task<bool> SeedAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemonstrationSeed>().RunAsync(cancellationToken);
    }
}
