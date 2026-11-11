using Microsoft.Extensions.FileProviders;
using Yarp.ReverseProxy.Configuration;

namespace SmartSentinelEye.ApiGateway;

/// <summary>
/// ADR-0192: the gateway also fronts the operator console at one origin. Hosting is opt-in: with no <c>Ui:Mode</c> the gateway
/// serves no page and an unrouted path is a 404, exactly as before (the "no accidental catch-all" guard in the integration stack
/// depends on it). <c>Ui:Mode=Proxy</c> (dev) routes the shell and the two federated remotes to their Vite dev servers.
/// <c>Ui:Mode=Static</c> serves the built bundles from three directories at the same paths: static files before the rate limiter
/// (plan D5), each marked <c>nosniff</c> (plan D9), and a fallback to the shell's <c>index.html</c> for a deep link.
/// </summary>
internal static class UiHosting
{
    internal const string ApiRoutePrefix = "api-";
    internal const string UiRoutePrefix = "ui-";

    private const string ModeKey = "Ui:Mode";
    private const string ShellCluster = "ui-shell";
    private const string CamerasCluster = "ui-cameras";
    private const string EventIngestionCluster = "ui-event-ingestion";

    // The shell route is a catch-all, so it must lose to every other route. A route's Order is compared before its
    // template, so a high number here keeps it last without depending on template precedence alone.
    private const int ShellRouteOrder = 1000;

    public static void AddGatewayUi(this WebApplicationBuilder builder, IReverseProxyBuilder proxy)
    {
        var mode = builder.Configuration[ModeKey];
        if (string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        if (mode.Equals("Proxy", StringComparison.OrdinalIgnoreCase))
        {
            proxy.LoadFromMemory(ProxyRoutes(), ProxyClusters(builder.Configuration));
            proxy.AddConfigFilter<NavigationRuleFilter>();
            return;
        }

        if (mode.Equals("Static", StringComparison.OrdinalIgnoreCase))
        {
            // Validated here, at configuration time, so a missing directory or entry file stops the gateway at start.
            _ = StaticRoots.Read(builder.Configuration);
            proxy.AddConfigFilter<NavigationRuleFilter>();
            return;
        }

        throw new InvalidOperationException($"{ModeKey} must be 'Proxy' or 'Static' when set; got '{mode}'.");
    }

    // Served before UseCors and UseRateLimiter, so a page load's many files never meet the per-source limiter. Each remote is
    // mounted under its prefix, the shell at the root; a request no file answers falls through to the rest of the pipeline.
    // PhysicalFileProvider keeps every request inside its directory and hides dot-files.
    public static void UseGatewayUiStatic(this WebApplication app)
    {
        if (!IsMode(app.Configuration, "Static"))
        {
            return;
        }

        var roots = StaticRoots.Read(app.Configuration);
        app.UseStaticFiles(StaticFiles(roots.Cameras, "/remotes/cameras"));
        app.UseStaticFiles(StaticFiles(roots.EventIngestion, "/remotes/event-ingestion"));
        app.UseStaticFiles(StaticFiles(roots.Shell, string.Empty));
    }

    // A path under /api or /remotes that no route claims is a 404 from the gateway. It must not fall through to the shell's
    // catch-all, which would answer a mistyped API call with HTML. Literal prefixes, so every more specific route still wins.
    public static void MapGatewayUi(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[ModeKey]))
        {
            return;
        }

        app.Map("/api/{**path}", () => Results.NotFound());
        app.Map("/remotes/{**path}", () => Results.NotFound());

        if (IsMode(app.Configuration, "Static"))
        {
            MapShellFallback(app, StaticRoots.Read(app.Configuration).Shell);
        }
    }

    // A deep link (a path with no file extension) is answered with the shell, which routes it client-side. A path with an
    // extension that no file answered is a real 404, so a missing script is never served as HTML. Lowest routing precedence, so
    // every route above wins first.
    private static void MapShellFallback(WebApplication app, string shellRoot)
    {
        var index = Path.Combine(shellRoot, "index.html");
        app.MapFallback(async context =>
        {
            var request = context.Request;
            var isRead = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);
            if (!isRead || Path.HasExtension(request.Path.Value ?? string.Empty))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        });
    }

    private static bool IsMode(IConfiguration configuration, string mode) =>
        string.Equals(configuration[ModeKey], mode, StringComparison.OrdinalIgnoreCase);

    private static StaticFileOptions StaticFiles(string root, string requestPath) =>
        new()
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = requestPath,
            OnPrepareResponse = context => context.Context.Response.Headers.XContentTypeOptions = "nosniff",
        };

    private sealed record StaticRoots(string Shell, string Cameras, string EventIngestion)
    {
        public static StaticRoots Read(IConfiguration configuration) =>
            new(
                Root(configuration, "Ui:Static:ShellRoot", "index.html"),
                Root(configuration, "Ui:Static:CamerasRoot", "remoteEntry.js"),
                Root(configuration, "Ui:Static:EventIngestionRoot", "remoteEntry.js"));

        private static string Root(IConfiguration configuration, string key, string entryFile)
        {
            var path = configuration[key];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException($"{key} must be set when {ModeKey} is 'Static'.");
            }

            if (!Directory.Exists(path))
            {
                throw new InvalidOperationException($"{key} '{path}' is not a directory.");
            }

            return File.Exists(Path.Combine(path, entryFile))
                ? Path.GetFullPath(path)
                : throw new InvalidOperationException($"{key} '{path}' has no {entryFile}.");
        }
    }

    // Remote paths are forwarded unchanged: each dev server runs with a Vite `base` equal to its prefix (plan D6), so it expects
    // /remotes/<name>/... and serves nothing at its own root. No RateLimiterPolicy and no CorsPolicy on a UI route (plan D5).
    private static IReadOnlyList<RouteConfig> ProxyRoutes() =>
    [
        new RouteConfig
        {
            RouteId = UiRoutePrefix + "remote-cameras",
            ClusterId = CamerasCluster,
            Match = new RouteMatch { Path = "/remotes/cameras/{**catch-all}" },
        },
        new RouteConfig
        {
            RouteId = UiRoutePrefix + "remote-event-ingestion",
            ClusterId = EventIngestionCluster,
            Match = new RouteMatch { Path = "/remotes/event-ingestion/{**catch-all}" },
        },
        new RouteConfig
        {
            RouteId = UiRoutePrefix + "shell",
            ClusterId = ShellCluster,
            Order = ShellRouteOrder,
            Match = new RouteMatch { Path = "/{**catch-all}" },
        },
    ];

    private static IReadOnlyList<ClusterConfig> ProxyClusters(IConfiguration configuration) =>
    [
        Cluster(ShellCluster, RequiredAddress(configuration, "Ui:Proxy:Shell")),
        Cluster(CamerasCluster, RequiredAddress(configuration, "Ui:Proxy:Cameras")),
        Cluster(EventIngestionCluster, RequiredAddress(configuration, "Ui:Proxy:EventIngestion")),
    ];

    private static ClusterConfig Cluster(string clusterIdentifier, string address) =>
        new()
        {
            ClusterId = clusterIdentifier,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["primary"] = new DestinationConfig { Address = address },
            },
        };

    private static string RequiredAddress(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } address
            ? address
            : throw new InvalidOperationException($"{key} must be set when {ModeKey} is 'Proxy'.");
}
