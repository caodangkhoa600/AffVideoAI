using System.Reflection;
using System.Security.Claims;
using AffiVideo.Application;
using AffiVideo.Infrastructure.Identity;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace AffiVideo.Api;

internal static class SessionSetup
{
    public const string OwnerPolicy = "Owner";
    public const string AntiforgeryHeader = "X-CSRF-TOKEN";

    /// <summary>
    /// Cookie sessions on the identity system, anti-forgery tokens, and an
    /// authorisation default of "signed in" that an endpoint has to opt out of.
    /// </summary>
    public static IServiceCollection AddSessions(this IServiceCollection services)
    {
        new IdentityBuilder(typeof(Member), services).AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies(cookies => cookies.ApplicationCookie!.Configure(options =>
            {
                options.Cookie.Name = "affivideo_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                // Lax, not Strict: the web app checks the session when a page is
                // opened, and a link from another site must still find the member signed in.
                options.Cookie.SameSite = SameSiteMode.Lax;
                // This is an API: it answers with a status, never a redirect to a page.
                options.Events.OnRedirectToLogin = context => Refuse(context.HttpContext, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Refuse(context.HttpContext, StatusCodes.Status403Forbidden);
            }));

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(OwnerPolicy, policy => policy.RequireRole(nameof(Domain.MemberRole.Owner)));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeader;
            options.Cookie.Name = "affivideo_csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Secure comes from the cookie policy below. Asking for it here makes the
            // anti-forgery system refuse to work when the web app reaches the API over HTTP.
        });

        // The last word on every cookie the API sets, whatever set it: never
        // readable by scripts, and only sent back over a secure connection. The
        // browser's connection is what matters, not the hop from the web app to the API.
        services.Configure<CookiePolicyOptions>(options =>
        {
            options.HttpOnly = HttpOnlyPolicy.Always;
            options.Secure = CookieSecurePolicy.Always;
        });

        // In the database, so that restarting or rebuilding the API does not sign everyone out.
        var dataProtection = services.AddDataProtection().SetApplicationName("AffiVideo");
        // The build starts the API with no database to write openapi.json; the keys are not needed for that.
        if (Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
        {
            dataProtection.PersistKeysToDbContext<AffiVideoDbContext>();
        }

        return services;
    }

    /// <summary>Goes where authorisation would go in the pipeline, and includes it.</summary>
    public static void UseSessions(this WebApplication app)
    {
        app.UseCookiePolicy();
        app.UseAuthentication();
        app.Use(IdentifyCaller);
        app.UseAuthorization();
        app.Use(RequireAntiforgeryToken);
    }

    // Tells the data-access layer whose Organization this request may see.
    private static Task IdentifyCaller(HttpContext context, RequestDelegate next)
    {
        if (context.User.FindFirstValue(MemberClaims.Organization) is { } organization)
        {
            context.RequestServices.GetRequiredService<Caller>().Identify(Guid.Parse(organization), context.User.MemberId());
        }
        return next(context);
    }

    // Every state-changing request, signing in included, must carry the token
    // from GET /api/v1/antiforgery-token. A page on another site cannot read it.
    private static async Task RequireAntiforgeryToken(HttpContext context, RequestDelegate next)
    {
        var method = context.Request.Method;
        var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
        if (!safe)
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "The anti-forgery token is missing or not valid.",
                    detail: $"Get a token from /api/v1/antiforgery-token and send it in the {AntiforgeryHeader} header.")
                    .ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }

    private static Task Refuse(HttpContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    public static Guid MemberId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
