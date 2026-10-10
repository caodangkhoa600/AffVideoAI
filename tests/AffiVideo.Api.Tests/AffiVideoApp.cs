using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AffiVideo.Api.Tests;
using AffiVideo.Application.Organizations;
using AffiVideo.Contracts;
using AffiVideo.Infrastructure;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(AffiVideoApp))]

namespace AffiVideo.Api.Tests;

/// <summary>
/// The API running in-process against real PostgreSQL and real object storage in
/// containers, prepared by the same code the migrate and seed commands run. Shared
/// by every test in the assembly. The worker joins them, in a container built from
/// its own Dockerfile, the first time a test needs a render.
/// </summary>
public sealed class AffiVideoApp : IAsyncLifetime
{
    // The same images compose.yaml runs.
    public const string PostgresImage = "postgres:17-alpine";
    private const string MinioImage =
        "cgr.dev/chainguard/minio@sha256:59667194421209c2c1eacbe761e24787e047985c5dfa96b15da9b59fe9b55cd0";

    private const string WorkerImage = "affivideo-worker:test";
    private const string Bucket = "affivideo-test";

    // The names the worker's container finds the other two under.
    private const string PostgresHost = "postgres";
    private const string MinioHost = "minio";

    private readonly INetwork _network;
    private readonly PostgreSqlContainer _postgres;
    private readonly MinioContainer _minio;
    private readonly Lazy<Task> _workerImage;
    private readonly Lazy<Task<IContainer>> _worker;
    private readonly ConcurrentDictionary<Type, Lazy<Task<object>>> _madeOnce = new();

    private WebApplicationFactory<Program>? _factory;

    public AffiVideoApp()
    {
        _network = new NetworkBuilder().Build();
        _postgres = new PostgreSqlBuilder(PostgresImage).WithNetwork(_network).WithNetworkAliases(PostgresHost).Build();
        _minio = new MinioBuilder(MinioImage).WithNetwork(_network).WithNetworkAliases(MinioHost).Build();
        _workerImage = new Lazy<Task>(BuildWorkerImageAsync);
        _worker = new Lazy<Task<IContainer>>(() => RunWorkerAsync(_postgres.GetConnectionString(), []));
    }

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
    /// <param name="affiliateLab">Whether the Organization has the Affiliate Lab, which only the system gives.</param>
    public Task<TestOrganization> CreateOrganizationAsync(bool affiliateLab = false) => CreateOrganizationAsync(_factory!, affiliateLab);

    /// <summary>Gives an Organization the Affiliate Lab, as the seed command does for the demonstration Organization.</summary>
    public async Task EnableAffiliateLabAsync(Guid organizationId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOrganizationProvisioner>()
            .EnableAffiliateLabAsync(organizationId, CancellationToken.None);
    }

    internal static async Task<TestOrganization> CreateOrganizationAsync(WebApplicationFactory<Program> factory, bool affiliateLab = false)
    {
        var unique = Guid.NewGuid().ToString("N");
        var owner = new Credentials($"owner-{unique}@example.test", $"owner-password-{unique}");
        await using var scope = factory.Services.CreateAsyncScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOrganizationProvisioner>()
            .CreateAsync($"Organization {unique}", owner.Email, owner.Password, CancellationToken.None);
        if (affiliateLab)
        {
            await scope.ServiceProvider.GetRequiredService<IOrganizationProvisioner>()
                .EnableAffiliateLabAsync(id!.Value, CancellationToken.None);
        }
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

    /// <summary>
    /// The worker, running in its container against the same database and object
    /// storage as the API. Built and started by the first test that asks; from then
    /// on it renders every job any test queues.
    /// </summary>
    public Task<IContainer> WorkerAsync() => _worker.Value;

    /// <summary>
    /// Runs a program inside the worker's container, which is where FFmpeg and
    /// ffprobe are, after putting these files there for it.
    /// </summary>
    public async Task<ExecResult> InWorkerAsync(IReadOnlyList<string> command, params (string Path, byte[] Content)[] files)
    {
        var worker = await WorkerAsync();
        foreach (var (path, content) in files)
        {
            await worker.CopyAsync(content, path, ct: TestContext.Current.CancellationToken);
        }
        return await worker.ExecAsync([.. command], TestContext.Current.CancellationToken);
    }

    /// <summary>The end of what the worker has logged, for saying why a render did not end as a test expected.</summary>
    public async Task<string> WorkerLogAsync() => await RenderStack.LogAsync(await WorkerAsync());

    /// <summary>
    /// Something slow that several tests only read, such as a Rendered Video, made by
    /// the first test that asks for it and shared with the rest.
    /// </summary>
    public async Task<T> OnceAsync<T>(Func<Task<T>> make) where T : class =>
        (T)await _madeOnce.GetOrAdd(typeof(T), _ => new Lazy<Task<object>>(async () => await make())).Value;

    /// <summary>
    /// The system a second time, for the tests about the queue itself: a database of
    /// its own in the same PostgreSQL, an API on it, and no worker but those the test
    /// starts. What is queued there is taken by nobody else.
    /// </summary>
    public async Task<RenderStack> NewStackAsync()
    {
        var name = $"queue_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var database = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
        var api = With(new Dictionary<string, string> { ["ConnectionStrings:Database"] = database });
        await api.Services.MigrateAsync(CancellationToken.None);
        return new RenderStack(api, settings => RunWorkerAsync(database, settings));
    }

    // The image compose.yaml builds, from the code as it is now. Docker's own cache keeps this short.
    private static async Task BuildWorkerImageAsync()
    {
        var root = CommonDirectoryPath.GetGitDirectory().DirectoryPath;
        var (exitCode, said) = await DockerAsync(
            "build", "--quiet", "-f", Path.Combine(root, "src", "AffiVideo.Worker", "Dockerfile"), "-t", WorkerImage, root);
        if (exitCode != 0) throw new InvalidOperationException($"The worker image could not be built:\n{said}");
    }

    /// <summary>Runs the docker command line, for what Testcontainers has no word for.</summary>
    internal static async Task<(int ExitCode, string Said)> DockerAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var docker = Process.Start(start)!;
        var output = docker.StandardOutput.ReadToEndAsync();
        var errors = await docker.StandardError.ReadToEndAsync();
        await docker.WaitForExitAsync();
        return (docker.ExitCode, $"{await output}\n{errors}");
    }

    /// <param name="databaseConnection">As the tests reach the database; the worker is given the name it has inside the network.</param>
    /// <param name="settings">Settings of the worker, named as its environment names them.</param>
    private async Task<IContainer> RunWorkerAsync(string databaseConnection, IReadOnlyCollection<(string Name, string Value)> settings)
    {
        await _workerImage.Value;
        var database = new NpgsqlConnectionStringBuilder(databaseConnection) { Host = PostgresHost, Port = PostgreSqlBuilder.PostgreSqlPort };
        var worker = new ContainerBuilder(WorkerImage)
            .WithNetwork(_network)
            .WithEnvironment("ConnectionStrings__Database", database.ConnectionString)
            .WithEnvironment("Storage__Endpoint", $"http://{MinioHost}:{MinioBuilder.MinioPort}")
            .WithEnvironment("Storage__AccessKey", _minio.GetAccessKey())
            .WithEnvironment("Storage__SecretKey", _minio.GetSecretKey())
            .WithEnvironment("Storage__Bucket", Bucket)
            .WithEnvironment(settings.ToDictionary(setting => setting.Name, setting => setting.Value))
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Worker started"))
            .Build();
        await worker.StartAsync();
        return worker;
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
        if (_worker.IsValueCreated)
        {
            try
            {
                await (await _worker.Value).DisposeAsync();
            }
            catch (Exception)
            {
                // It never started; the test that asked for it has already said why.
            }
        }
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
        await _network.DisposeAsync();
    }
}

public sealed record Credentials(string Email, string Password);

public sealed record TestOrganization(Guid Id, Credentials Owner);
