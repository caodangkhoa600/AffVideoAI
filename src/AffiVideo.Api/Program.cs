using AffiVideo.Api;
using AffiVideo.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // A constructor parameter is a required property, in requests and in the
    // OpenAPI description the web app's types are generated from.
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
    options.SerializerOptions.RespectNullableAnnotations = true;
});

var app = builder.Build();

// `dotnet AffiVideo.Api.dll migrate` is the only thing that changes the schema.
if (args is ["migrate"])
{
    await app.Services.MigrateAsync(CancellationToken.None);
    app.Logger.LogInformation("Database migrations applied and storage bucket present");
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
// Liveness for the container's health check: answers whenever the process is up.
app.MapGet("/health", () => Results.Ok()).ExcludeFromDescription();

var v1 = app.MapGroup("/api/v1");
v1.MapStatus();

app.Run();

public partial class Program;
