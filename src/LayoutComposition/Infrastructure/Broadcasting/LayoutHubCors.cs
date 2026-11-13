using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// The kiosk calls the hub on this service's own origin from another origin. Only the hub path answers a browser's CORS
/// preflight, and only for the configured origins; REST endpoints stay closed to browsers on other origins.
/// </summary>
public static class LayoutHubCors
{
    public const string AllowedOriginsKey = "LayoutHub:Cors:AllowedOrigins";

    private const string PolicyName = "layout-hub";

    // A preflight is cached per origin and URL: WebSockets and SSE ask once per connection set-up. Long polling cannot use the
    // cache, because every poll URL carries a timestamp, so each poll pays its own preflight.
    private static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    private static readonly string[] AllowedHeaders = ["Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent"];

    private static readonly string[] AllowedMethods = ["GET", "POST", "DELETE"];

    public static WebApplicationBuilder AddLayoutHubCors(this WebApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        var origins = builder.Configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{AllowedOriginsKey} must list exact origins; a wildcard is not allowed: '{origin}'.");
            }

            if (!IsHttpOrigin(origin))
            {
                throw new InvalidOperationException(
                    $"{AllowedOriginsKey} must list absolute http(s) origins with no path, query or trailing slash: '{origin}'.");
            }
        }

        builder.Services.AddCors(options =>
            options.AddPolicy(
                PolicyName,
                policy => policy
                    .WithOrigins(origins)
                    .WithHeaders(AllowedHeaders)
                    .WithMethods(AllowedMethods)
                    .AllowCredentials()
                    .SetPreflightMaxAge(PreflightMaxAge)));

        return builder;
    }

    private static bool IsHttpOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.UserInfo.Length == 0
        && uri.AbsolutePath == "/"
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && !origin.EndsWith('/');

    // Before authentication: a preflight carries no token, and the hub's authorization would refuse it.
    public static WebApplication UseLayoutHubCors(this WebApplication app)
    {
        Ensure.That(app).IsNotNull();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(LayoutLifecycleHub.Path),
            branch => branch.UseCors(PolicyName));

        return app;
    }
}
