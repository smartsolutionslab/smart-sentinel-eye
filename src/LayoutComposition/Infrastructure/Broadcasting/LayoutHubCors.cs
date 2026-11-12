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

    // A preflight is cached per origin and URL, so the browser asks once per connection set-up and not per message.
    private static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    private static readonly string[] AllowedHeaders = ["Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent"];

    private static readonly string[] AllowedMethods = ["GET", "POST", "DELETE"];

    public static WebApplicationBuilder AddLayoutHubCors(this WebApplicationBuilder builder)
    {
        Ensure.That(builder).IsNotNull();

        var origins = builder.Configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
        if (origins.Any(origin => origin.Contains('*', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{AllowedOriginsKey} must list exact origins; a wildcard is not allowed.");
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
