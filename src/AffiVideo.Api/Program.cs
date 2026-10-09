using System.Text.Json;
using System.Text.Json.Serialization;
using AffiVideo.Api;
using AffiVideo.Infrastructure;
using Microsoft.AspNetCore.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSessions();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // A field is named in a 400 as it is named in the request: "originalUrl", not "OriginalUrl".
    if (context.ProblemDetails is HttpValidationProblemDetails { Errors: var errors } refused)
    {
        refused.Errors = errors
            .GroupBy(field => JsonNamingPolicy.CamelCase.ConvertName(field.Key), field => field.Value)
            .ToDictionary(field => field.Key, field => field.SelectMany(messages => messages).ToArray());
    }
});
// A request that cannot be read (a status that does not exist, a page that is not
// a number) is a 400 in every environment, not an exception in Development.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
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
    // And a name is a name: a creative template is "ProductShowcase", never 1.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
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
        ? "Demonstration data created"
        : "Demonstration data already present; nothing changed");
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
v1.MapProducts();
v1.MapProductAssets();
v1.MapFacts();
v1.MapProjects();
v1.MapVariants();
v1.MapStoryboards();

app.Run();

public partial class Program;
