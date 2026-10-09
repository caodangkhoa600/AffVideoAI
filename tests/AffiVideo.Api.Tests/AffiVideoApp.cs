using System.Text.Json;
using System.Text.Json.Serialization;
using AffiVideo.Api.Tests;
using AffiVideo.Application.Organizations;
using AffiVideo.Contracts;
using AffiVideo.Infrastructure;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(AffiVideoApp))]

namespace AffiVideo.Api.Tests;

/// <summary>
/// The API running in-process against real PostgreSQL and real object storage in
/// containers, prepared by the same code the migrate and seed commands run. Shared
/// by every test in the assembly.
/// </summary>
public sealed class AffiVideoApp : IAsyncLifetime
{
    // The same images compose.yaml runs.
    public const string PostgresImage = "postgres:17-alpine";
    private const string MinioImage =
        "cgr.dev/chainguard/minio@sha256:59667194421209c2c1eacbe761e24787e047985c5dfa96b15da9b59fe9b55cd0";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(PostgresImage).Build();
    private readonly MinioContainer _minio = new MinioBuilder(MinioImage).Build();
    private const string Bucket = "affivideo-test";

    private WebApplicationFactory<Program>? _factory;

    /// <summary>How the API writes JSON: camelCase names, enums by name.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
        _factory = With([]);
        await _factory.Services.MigrateAsync(CancellationToken.None);
        await _factory.Services.SeedAsync(CancellationToken.None);
    }

    public HttpClient CreateClient() => _factory!.CreateClient();

    /// <summary>A browser with no cookies yet.</summary>
    public Browser NewBrowser() => NewBrowser(_factory!);

    // https, because a browser only sends a Secure cookie back over a secure connection.
    public static Browser NewBrowser(WebApplicationFactory<Program> factory) =>
        new(factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }));

    /// <summary>
    /// A new Organization with one Owner, made by the code the seed command uses:
    /// sign-up is closed, so there is no way to make one over HTTP.
    /// </summary>
    public async Task<TestOrganization> CreateOrganizationAsync()
    {
        var unique = Guid.NewGuid().ToString("N");
        var owner = new Credentials($"owner-{unique}@example.test", $"owner-password-{unique}");
        await using var scope = _factory!.Services.CreateAsyncScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOrganizationProvisioner>()
            .CreateAsync($"Organization {unique}", owner.Email, owner.Password, CancellationToken.None);
        return new TestOrganization(id!.Value, owner);
    }

    /// <summary>Runs what the seed command runs, again. Answers whether it created anything.</summary>
    public Task<bool> SeedAsync() => _factory!.Services.SeedAsync(CancellationToken.None);

    /// <summary>Has a signed-in Owner add an Editor, and returns what the Editor signs in with.</summary>
    public async Task<Credentials> AddEditorAsync(Browser owner, TestOrganization organization)
    {
        var unique = Guid.NewGuid().ToString("N");
        var editor = new Credentials($"editor-{unique}@example.test", $"editor-password-{unique}");
        var response = await owner.PostAsync(
            $"/api/v1/organizations/{organization.Id}/members", new AddMemberRequest(editor.Email, editor.Password));
        response.EnsureSuccessStatusCode();
        return editor;
    }

    /// <summary>A browser signed in as these credentials.</summary>
    public async Task<Browser> SignedInAsync(Credentials credentials)
    {
        var browser = NewBrowser();
        (await browser.SignInAsync(credentials.Email, credentials.Password)).EnsureSuccessStatusCode();
        return browser;
    }

    /// <summary>The keys of the files in object storage that start with this, read from the storage itself.</summary>
    public async Task<string[]> StoredKeysAsync(string prefix)
    {
        using var storage = new AmazonS3Client(
            new BasicAWSCredentials(_minio.GetAccessKey(), _minio.GetSecretKey()),
            new AmazonS3Config { ServiceURL = _minio.GetConnectionString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        var listed = await storage.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix }, TestContext.Current.CancellationToken);
        return listed.S3Objects?.Select(stored => stored.Key).ToArray() ?? [];
    }

    /// <summary>Where a file would be for someone who went to the object storage directly, around the API.</summary>
    public Uri StorageAddress(string key) => new($"{_minio.GetConnectionString().TrimEnd('/')}/{Bucket}/{key}");

    /// <summary>Another instance of the API with some settings replaced, for tests about a broken environment.</summary>
    public WebApplicationFactory<Program> With(Dictionary<string, string> settings)
    {
        var all = new Dictionary<string, string>
        {
            ["ConnectionStrings:Database"] = _postgres.GetConnectionString(),
            ["Storage:Endpoint"] = _minio.GetConnectionString(),
            ["Storage:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:Bucket"] = Bucket,
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

public sealed record Credentials(string Email, string Password);

public sealed record TestOrganization(Guid Id, Credentials Owner);
