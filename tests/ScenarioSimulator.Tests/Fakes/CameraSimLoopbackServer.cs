using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// A camera-sim stand-in on a real socket (127.0.0.1, free port). A stub primary handler would
/// bypass SocketsHttpHandler, which is where the runtime starts the System.Net.Http client span,
/// so a span test against a stub would assert over zero spans. Every request is recorded as
/// "METHOD /path" and answered by <c>respond</c> from its zero-based position.
/// </summary>
internal sealed class CameraSimLoopbackServer : IAsyncDisposable
{
    private readonly WebApplication app;

    private readonly List<string> requests = [];

    private CameraSimLoopbackServer(WebApplication app, Uri baseAddress)
    {
        this.app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (requests)
            {
                return [.. requests];
            }
        }
    }

    public static async Task<CameraSimLoopbackServer> StartAsync(Func<int, (int Status, string Body)> respond)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        CameraSimLoopbackServer? server = null;

        app.Run(async context =>
        {
            int position;
            lock (server!.requests)
            {
                position = server.requests.Count;
                server.requests.Add($"{context.Request.Method} {context.Request.Path}");
            }

            var (status, body) = respond(position);
            context.Response.StatusCode = status;
            await context.Response.WriteAsync(body);
        });

        // The base address is only known once the server is listening; the handler above
        // reads `server` only per request, after it is assigned below.
        await app.StartAsync();
        var address = app.Urls.First();
        server = new CameraSimLoopbackServer(app, new Uri(address.EndsWith('/') ? address : address + "/"));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
