using Yarp.ReverseProxy.Configuration;

namespace SmartSentinelEye.ApiGateway;

/// <summary>
/// Fronts the console at the gateway's origin (ADR-0192). Opt-in: with no <c>Ui:Mode</c> the gateway serves no page and an unrouted
/// path stays a 404. <c>Proxy</c> forwards to the Vite dev servers; <c>Static</c> serves the built bundles.
/// </summary>
internal static class UiHosting
{
    internal const string ApiRoutePrefix = "api-";
    internal const string UiRoutePrefix = "ui-";

    private const string ModeKey = "Ui:Mode";
    private const string ShellCluster = "ui-shell";
    private const string CamerasCluster = "ui-cameras";
    private const string EventIngestionCluster = "ui-event-ingestion";
    private const string KioskCluster = "ui-kiosk";
    private const string WallCluster = "ui-wall";
    private const string KioskPrefix = "/kiosk";
    private const string WallPrefix = "/wall";

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

    // Before UseRateLimiter, so a page load's many files never meet the per-source limiter.
    public static void UseGatewayUiStatic(this WebApplication app)
    {
        if (!IsMode(app.Configuration, "Static"))
        {
            return;
        }

        var roots = StaticRoots.Read(app.Configuration);
        roots.Kiosk.ServeFiles(app);
        roots.Wall.ServeFiles(app);
        roots.Cameras.ServeFiles(app);
        roots.EventIngestion.ServeFiles(app);
        roots.Shell.ServeFiles(app);
    }

    // An unclaimed /api or /remotes path is a 404, not the shell's HTML.
    public static void MapGatewayUi(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[ModeKey]))
        {
            return;
        }

        app.Map("/api/{**path}", () => Results.NotFound());
        app.Map("/remotes/{**path}", () => Results.NotFound());

        MapSlashRedirect(app, KioskPrefix);
        MapSlashRedirect(app, WallPrefix);

        if (IsMode(app.Configuration, "Static"))
        {
            var roots = StaticRoots.Read(app.Configuration);
            roots.Kiosk.MapFallback(app);
            roots.Wall.MapFallback(app);
            roots.Shell.MapFallback(app);
        }
    }

    // Middleware, not an endpoint: a "/kiosk" endpoint also matches "/kiosk/" and would redirect it to itself. The dev server
    // would 404 the bare prefix in proxy mode and the shell fallback would answer it in static mode.
    private static void MapSlashRedirect(WebApplication app, string prefix) =>
        app.Use(
            (context, next) =>
            {
                var request = context.Request;
                if (HttpMethods.IsGet(request.Method) && string.Equals(request.Path.Value, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Redirect($"{prefix}/{request.QueryString}");
                    return Task.CompletedTask;
                }

                return next(context);
            });

    private static bool IsMode(IConfiguration configuration, string mode) =>
        string.Equals(configuration[ModeKey], mode, StringComparison.OrdinalIgnoreCase);

    private sealed record StaticRoots(UiRoot Shell, UiRoot Cameras, UiRoot EventIngestion, UiRoot Kiosk, UiRoot Wall)
    {
        public static StaticRoots Read(IConfiguration configuration) =>
            new(
                Root(configuration, "Ui:Static:ShellRoot", string.Empty, "index.html"),
                Root(configuration, "Ui:Static:CamerasRoot", "/remotes/cameras", "remoteEntry.js"),
                Root(configuration, "Ui:Static:EventIngestionRoot", "/remotes/event-ingestion", "remoteEntry.js"),
                Root(configuration, "Ui:Static:KioskRoot", KioskPrefix, "index.html"),
                Root(configuration, "Ui:Static:WallRoot", WallPrefix, "index.html"));

        private static UiRoot Root(IConfiguration configuration, string key, string requestPath, string entryFile)
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
                ? new UiRoot(requestPath, Path.GetFullPath(path), entryFile)
                : throw new InvalidOperationException($"{key} '{path}' has no {entryFile}.");
        }
    }

    // Forwarded unchanged: each dev server runs with a Vite `base` equal to its prefix. UI routes carry no rate-limit or CORS policy.
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
            RouteId = UiRoutePrefix + "kiosk",
            ClusterId = KioskCluster,
            Match = new RouteMatch { Path = KioskPrefix + "/{**catch-all}" },
        },
        new RouteConfig
        {
            RouteId = UiRoutePrefix + "wall",
            ClusterId = WallCluster,
            Match = new RouteMatch { Path = WallPrefix + "/{**catch-all}" },
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
        Cluster(KioskCluster, RequiredAddress(configuration, "Ui:Proxy:Kiosk")),
        Cluster(WallCluster, RequiredAddress(configuration, "Ui:Proxy:Wall")),
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
