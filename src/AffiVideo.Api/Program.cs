using System.Text.Json.Serialization;
using AffiVideo.Api;
using AffiVideo.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSessions();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // A constructor parameter is a required property, in requests and in the
    // OpenAPI description the web app's types are generated from.
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.RespectNullableAnnotations = true;
    // A number is a number: without this the description offers "integer or string" for every count.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// `dotnet AffiVideo.Api.dll migrate` is the only thing that changes the schema.
if (args is ["migrate"])
{
    await app.Services.MigrateAsync(CancellationToken.None);
    app.Logger.LogInformation("Database migrations applied and storage bucket present");
    return;
}

// `dotnet AffiVideo.Api.dll seed` creates the demonstration Organization; see the README.
if (args is ["seed"])
{
    var created = await app.Services.SeedAsync(CancellationToken.None);
    app.Logger.LogInformation("{Outcome}", created
        ? "Demonstration Organization created"
        : "Demonstration Organization already present; nothing changed");
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSessions();

// Every endpoint needs a signed-in member unless it says otherwise.
app.MapOpenApi().AllowAnonymous();
// Liveness for the container's health check: answers whenever the process is up.
app.MapGet("/health", () => Results.Ok()).AllowAnonymous().ExcludeFromDescription();

var v1 = app.MapGroup("/api/v1");
v1.MapStatus();
v1.MapSession();
v1.MapOrganizations();

app.Run();

public partial class Program;
