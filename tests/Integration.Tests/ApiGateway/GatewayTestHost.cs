using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using GatewayEntryPoint = global::SmartSentinelEye.ApiGateway.GatewayHost;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Starts the gateway's real composition (<c>GatewayHost.Build</c>, the same one <c>Program</c> runs) on a loopback port, with its
/// own <c>appsettings.json</c> as the base and a test's overrides on top. No Aspire, no Docker: destinations are
/// <see cref="StubDestination"/>s, so a test can assert what the gateway routed where and what it forwarded.
/// </summary>
internal sealed class GatewayTestHost : IAsyncDisposable
{
    public static readonly IReadOnlyList<string> Contexts =
    [
        "camera-catalog",
        "stream-distribution",
        "layout-composition",
        "event-ingestion",
        "overlay-designer",
        "system-variables",
        "audit-observability",
        "automation",
        "identity",
    ];

    private readonly WebApplication application;

    private GatewayTestHost(WebApplication application, Uri address)
    {
        this.application = application;
        Address = address;
        Client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = address };
    }

    public Uri Address { get; }

    public HttpClient Client { get; }

    /// <summary>Points every context's cluster at one destination (the gateway's own configuration names them <c>http://{context}</c>).</summary>
    public static void PointContextsAt(IDictionary<string, string?> overrides, Uri destination)
    {
        foreach (var context in Contexts)
        {
            overrides[$"ReverseProxy:Clusters:{context}:Destinations:primary:Address"] = destination.ToString();
        }
    }

    public static async Task<GatewayTestHost> StartAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        Dictionary<string, string?> overrides = [];
        configure?.Invoke(overrides);

        var application = GatewayEntryPoint.Build(
            [$"--contentRoot={GatewayProjectDirectory()}"],
            builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(overrides);
            });

        await application.StartAsync();
        var address = new Uri(application.Urls.First());

        return new GatewayTestHost(application, address);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await application.StopAsync();
        await application.DisposeAsync();
    }

    internal static string GatewayProjectDirectory()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        var root = candidate?.FullName
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");

        return System.IO.Path.Combine(root, "src", "ApiGateway");
    }
}
