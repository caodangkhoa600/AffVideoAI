using AffiVideo.Application;
using AffiVideo.Application.Generation;
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
using AffiVideo.Infrastructure.Generation;
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
using Microsoft.Extensions.Options;

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
        services.Configure<GenerationOptions>(configuration.GetSection(GenerationOptions.Section));
        services.AddScoped<IStoryboards, ScopedStoryboards>();
        // What the estimate of a generation is made with. The worker checks the same rates as it starts.
        services.Configure<ProductionCostOptions>(configuration.GetSection(ProductionCostOptions.Section));
        services.AddScoped<IGeneration, ScopedGeneration>();
        services.AddScoped<IRenders, ScopedRenders>();
        services.AddScoped<IRenderedVideos, ScopedRenderedVideos>();
        services.AddScoped<ILabProducts, ScopedLabProducts>();
        services.AddScoped<ICampaigns, ScopedCampaigns>();
        services.AddScoped<ISocialAccounts, ScopedSocialAccounts>();
        services.AddScoped<IAffiliateLinks, ScopedAffiliateLinks>();
        services.AddScoped<IPublishedPosts, ScopedPublishedPosts>();
        services.AddScoped<IPerformanceSnapshots, ScopedPerformanceSnapshots>();
        services.AddScoped<ICommissionRecords, ScopedCommissionRecords>();
        services.Configure<LabDashboardOptions>(configuration.GetSection(LabDashboardOptions.Section));
        services.AddScoped<ILabDashboard, ScopedLabDashboard>();
        services.AddScoped<DemonstrationSeed>();
        return services;
    }

    /// <summary>
    /// The render queue and the queues of Stills and Clips as the worker sees them, across Organizations, and the
    /// image provider. Only the worker asks for this: nothing in the API can claim a job or call a provider.
    /// The video provider is the worker's own to register: its fake needs FFmpeg, which only the worker has.
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
        // The Stills a member confirmed wait in a queue of their own, served under the same leases.
        services.AddScoped<IStillQueue, StillQueue>();
        services.AddScoped<IClipQueue, ClipQueue>();
        // Whatever is configured to make them. A real provider is registered in the fake's place, never beside it.
        services.AddSingleton<IImageGenerator>(provider =>
            provider.GetRequiredService<IOptions<GenerationOptions>>().Value switch
            {
                { ImageProvider: ImageProvider.Fake, Fake: var fake } => new FakeImageGenerator(fake),
                _ => new NoImageGenerator(),
            });
        // Speech waits in a queue of its own too, and the fake that makes it needs nothing the API lacks.
        services.AddScoped<ISpeechQueue, SpeechQueue>();
        services.AddSingleton<ITextToSpeech>(provider =>
            provider.GetRequiredService<IOptions<GenerationOptions>>().Value switch
            {
                { SpeechProvider: SpeechProvider.Fake } options => new FakeTextToSpeech(options),
                _ => new NoTextToSpeech(),
            });
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
