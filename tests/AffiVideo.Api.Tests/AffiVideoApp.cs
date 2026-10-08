using AffiVideo.Api.Tests;
using AffiVideo.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(AffiVideoApp))]

namespace AffiVideo.Api.Tests;

/// <summary>
/// The API running in-process against real PostgreSQL and real object storage in
/// containers, prepared by the same code the migrate command runs. Shared by every
/// test in the assembly.
/// </summary>
public sealed class AffiVideoApp : IAsyncLifetime
{
    // The same images compose.yaml runs.
    public const string PostgresImage = "postgres:17-alpine";
    private const string MinioImage =
        "cgr.dev/chainguard/minio@sha256:59667194421209c2c1eacbe761e24787e047985c5dfa96b15da9b59fe9b55cd0";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly MinioContainer _minio = new MinioBuilder(MinioImage).Build();
    private WebApplicationFactory<Program>? _factory;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
        _factory = With([]);
        await _factory.Services.MigrateAsync(CancellationToken.None);
    }

    public HttpClient CreateClient() => _factory!.CreateClient();

    /// <summary>Another instance of the API with some settings replaced, for tests about a broken environment.</summary>
    public WebApplicationFactory<Program> With(Dictionary<string, string> settings)
    {
        var all = new Dictionary<string, string>
        {
            ["ConnectionStrings:Database"] = _postgres.GetConnectionString(),
            ["Storage:Endpoint"] = _minio.GetConnectionString(),
            ["Storage:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:Bucket"] = "affivideo-test",
        };
        foreach (var (key, value) in settings) all[key] = value;

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in all) builder.UseSetting(key, value);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
    }
}
