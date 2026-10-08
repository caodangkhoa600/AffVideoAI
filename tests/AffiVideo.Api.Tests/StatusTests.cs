using System.Net;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using Testcontainers.PostgreSql;

namespace AffiVideo.Api.Tests;

public sealed class StatusTests(AffiVideoApp app)
{
    private const string NoDatabaseThere = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x";
    private const string NoStorageThere = "http://127.0.0.1:1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Status_reports_the_database_and_object_storage_as_reachable()
    {
        using var client = app.CreateClient();

        var response = await client.GetAsync("/api/v1/status", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<StatusResponse>(Cancellation);
        Assert.NotNull(status);
        Assert.True(status.Database.Reachable);
        Assert.True(status.ObjectStorage.Reachable);
    }

    [Fact]
    public async Task Status_reports_a_database_that_cannot_be_reached()
    {
        await using var broken = app.With(new() { ["ConnectionStrings:Database"] = NoDatabaseThere });
        using var client = broken.CreateClient();

        var status = await client.GetFromJsonAsync<StatusResponse>("/api/v1/status", Cancellation);

        Assert.NotNull(status);
        Assert.False(status.Database.Reachable);
        Assert.True(status.ObjectStorage.Reachable);
    }

    [Fact]
    public async Task Status_notices_a_database_that_goes_away_after_it_was_reachable()
    {
        await using var postgres = new PostgreSqlBuilder(AffiVideoApp.PostgresImage).Build();
        await postgres.StartAsync(Cancellation);
        await using var ownDatabase = app.With(new() { ["ConnectionStrings:Database"] = postgres.GetConnectionString() });
        using var client = ownDatabase.CreateClient();
        var before = await client.GetFromJsonAsync<StatusResponse>("/api/v1/status", Cancellation);

        await postgres.StopAsync(Cancellation);
        var after = await client.GetFromJsonAsync<StatusResponse>("/api/v1/status", Cancellation);

        Assert.True(before!.Database.Reachable);
        Assert.False(after!.Database.Reachable);
    }

    [Fact]
    public async Task Status_reports_object_storage_that_cannot_be_reached()
    {
        await using var broken = app.With(new() { ["Storage:Endpoint"] = NoStorageThere });
        using var client = broken.CreateClient();

        var status = await client.GetFromJsonAsync<StatusResponse>("/api/v1/status", Cancellation);

        Assert.NotNull(status);
        Assert.True(status.Database.Reachable);
        Assert.False(status.ObjectStorage.Reachable);
    }

    [Fact]
    public async Task Status_reports_object_storage_that_refuses_the_credentials_as_unreachable()
    {
        await using var broken = app.With(new() { ["Storage:SecretKey"] = "not-the-secret" });
        using var client = broken.CreateClient();

        var status = await client.GetFromJsonAsync<StatusResponse>("/api/v1/status", Cancellation);

        Assert.NotNull(status);
        Assert.False(status.ObjectStorage.Reachable);
    }

    [Fact]
    public async Task Health_answers_even_when_the_database_and_object_storage_are_down()
    {
        await using var broken = app.With(new()
        {
            ["ConnectionStrings:Database"] = NoDatabaseThere,
            ["Storage:Endpoint"] = NoStorageThere,
        });
        using var client = broken.CreateClient();

        var response = await client.GetAsync("/health", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_api_publishes_an_OpenAPI_description_that_includes_status()
    {
        using var client = app.CreateClient();

        var document = await client.GetStringAsync("/openapi/v1.json", Cancellation);

        Assert.Contains("/api/v1/status", document);
    }
}
