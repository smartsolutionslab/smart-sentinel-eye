using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms.Builder;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Spec 311 (#2283): <c>X-Fab</c> is a header the caller chose, and the gateway
/// verifies no token, so a caller-supplied value must not reach the services
/// behind it looking as though the edge vouched for it. Every proxied route
/// strips it on the way through, and still forwards <c>Authorization</c> — the
/// credential each service validates for itself (ADR-0106).
///
/// <para>
/// Reads <c>src/ApiGateway/appsettings.json</c> through YARP's own config
/// loader and transform builder, then runs each route's real request
/// transformer — not a text match on the JSON, which would pass for a
/// misspelt transform key YARP silently ignores. No stack boot.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayEdgeHeaderTransformTests
{
    private const int ProxiedRouteCount = 9;

    private const string GatewaySettings = "src/ApiGateway/appsettings.json";

    [Fact]
    public async Task Every_proxied_route_strips_an_inbound_X_Fab_and_forwards_Authorization()
    {
        using ServiceProvider services = GatewayProxyServices();

        IReadOnlyList<RouteConfig> routes = services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes;
        ITransformBuilder transforms = services.GetRequiredService<ITransformBuilder>();

        routes.Count.ShouldBeGreaterThanOrEqualTo(
            ProxiedRouteCount,
            $"only {routes.Count} routes loaded from {GatewaySettings} — the scan is broken, not "
            + "passing, because a check over no routes observes nothing.");

        List<string> forwardingXFab = [];
        List<string> droppingAuthorization = [];

        foreach (RouteConfig route in routes)
        {
            using HttpRequestMessage outbound = await TransformAsync(transforms.Build(route, cluster: null), route);

            if (outbound.Headers.Contains("X-Fab"))
            {
                forwardingXFab.Add(route.RouteId);
            }

            if (!outbound.Headers.Contains("Authorization"))
            {
                droppingAuthorization.Add(route.RouteId);
            }
        }

        forwardingXFab.ShouldBeEmpty(
            $"routes forwarding a caller-supplied X-Fab: [{string.Join(", ", forwardingXFab)}]. "
            + "The gateway verifies no fab claim, so it must not pass one along as if it had.");

        droppingAuthorization.ShouldBeEmpty(
            $"routes dropping Authorization: [{string.Join(", ", droppingAuthorization)}]. Each "
            + "service validates the bearer token itself; stripping X-Fab must not take it too.");
    }

    private static async Task<HttpRequestMessage> TransformAsync(HttpTransformer transformer, RouteConfig route)
    {
        DefaultHttpContext context = new();
        context.Request.Method = HttpMethods.Get;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("gateway.test");
        context.Request.Path = $"/{route.RouteId}/health";
        context.Request.Headers["X-Fab"] = "forged";
        context.Request.Headers.Authorization = "Bearer x";

        HttpRequestMessage outbound = new(HttpMethod.Get, "http://destination.test/");
        await transformer.TransformRequestAsync(context, outbound, "http://destination.test", CancellationToken.None);
        return outbound;
    }

    private static ServiceProvider GatewayProxyServices()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(GatewaySettingsPath(), optional: false)
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Built with <see cref="System.IO.Path.Combine(string[])"/> rather than a
    /// literal path: a backslash separator is green on Windows and red on Linux
    /// CI, and this repository has been bitten by exactly that.
    /// </summary>
    private static string GatewaySettingsPath() =>
        System.IO.Path.Combine([RepositoryRoot(), .. GatewaySettings.Split('/')]);

    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
