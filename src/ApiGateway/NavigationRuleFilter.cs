using Yarp.ReverseProxy.Configuration;

namespace SmartSentinelEye.ApiGateway;

/// <summary>
/// ADR-0192 / spec 491, Q1 option A. When the gateway hosts the UI, the shell owns pages at <c>/system-variables</c> and
/// <c>/event-ingestion/...</c>, the same first segment as two bare REST routes. A bare REST route therefore answers only requests
/// that are not page navigations: a browser reload or typed address sends <c>Sec-Fetch-Dest: document</c>, which no bare route
/// matches, so the request falls through to the shell. <c>fetch</c>, scripts and test clients send no such value (or another one)
/// and still reach the REST service. The <c>/api</c> twins and the UI's own routes are left alone.
///
/// <para>
/// It is a filter, registered only when the UI is hosted, so a gateway with no <c>Ui</c> section keeps answering a browser that
/// opens a REST path directly (for example a <c>/health</c> check), exactly as before.
/// </para>
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
