using AffiVideo.Application.SystemStatus;
using AffiVideo.Infrastructure.Persistence;
using AffiVideo.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AffiVideo.Infrastructure;

public static class InfrastructureSetup
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AffiVideoDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Database")));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton<S3ObjectStorage>();
        services.AddScoped<ISystemStatusReader, SystemStatusReader>();
        return services;
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
}
