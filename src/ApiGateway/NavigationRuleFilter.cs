using Yarp.ReverseProxy.Configuration;

namespace SmartSentinelEye.ApiGateway;

/// <summary>
/// Makes a bare REST route answer only non-navigations, because the shell owns pages at the same first segment (ADR-0192). A
/// reload sends <c>Sec-Fetch-Dest: document</c>, which no bare route matches, so it falls through to the shell. Registered only
/// when the UI is hosted.
/// </summary>
internal sealed class NavigationRuleFilter : IProxyConfigFilter
{
    private static readonly RouteHeader NotAPageNavigation = new()
    {
        Name = "Sec-Fetch-Dest",
        Values = ["document"],
        Mode = HeaderMatchMode.NotContains,
    };

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) => new(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        if (!IsBareRestRoute(route))
        {
            return new(route);
        }

        List<RouteHeader> headers = [.. route.Match.Headers ?? [], NotAPageNavigation];

        return new(route with { Match = route.Match with { Headers = headers } });
    }

    private static bool IsBareRestRoute(RouteConfig route) =>
        !route.RouteId.StartsWith(UiHosting.ApiRoutePrefix, StringComparison.Ordinal)
        && !route.RouteId.StartsWith(UiHosting.UiRoutePrefix, StringComparison.Ordinal);
}
