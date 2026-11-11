using Yarp.ReverseProxy.Configuration;

namespace SmartSentinelEye.ApiGateway;

/// <summary>
/// ADR-0192: the gateway also fronts the operator console at one origin. Hosting is opt-in: with no <c>Ui:Mode</c> the gateway
/// serves no page and an unrouted path is a 404, exactly as before (the "no accidental catch-all" guard in the integration stack
/// depends on it). <c>Ui:Mode=Proxy</c> (dev) routes the shell and the two federated remotes to their Vite dev servers.
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

        throw new InvalidOperationException($"{ModeKey} must be 'Proxy' when set; got '{mode}'.");
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
