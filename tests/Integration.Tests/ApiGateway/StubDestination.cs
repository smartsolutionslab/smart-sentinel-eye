using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>What a <see cref="StubDestination"/> saw: the path the gateway actually forwarded, and every header it sent.</summary>
internal sealed record StubRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// A throwaway HTTP destination for gateway tests that do not boot the Aspire stack. It answers every request with a JSON echo of
/// what it received, tagged with its own name so a test can tell which destination the gateway chose, and echoes WebSocket text
/// frames back prefixed with the name. It lives in the test project, so the gateway assembly stays free of any WebSocket
/// dependency (BoundaryTests inspects the gateway assembly only).
/// </summary>
internal sealed class StubDestination : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly ConcurrentQueue<StubRequest> requests;

    private StubDestination(string name, WebApplication application, ConcurrentQueue<StubRequest> requests, Uri address)
    {
        Name = name;
        this.application = application;
        this.requests = requests;
        Address = address;
    }

    public string Name { get; }

    public Uri Address { get; }

    public IReadOnlyCollection<StubRequest> Requests => requests;

    public static async Task<StubDestination> StartAsync(string name)
    {
        ConcurrentQueue<StubRequest> seen = [];
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var application = builder.Build();

        application.UseWebSockets();
        application.Run(async context =>
        {
            var headers = context.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var path = context.Request.Path.Value ?? string.Empty;
            var query = context.Request.QueryString.Value ?? string.Empty;
            seen.Enqueue(new StubRequest(context.Request.Method, path, query, headers));

            if (context.WebSockets.IsWebSocketRequest)
            {
                var subProtocol = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
                using var socket = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
                await EchoAsync(socket, name, context.RequestAborted);
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { name, method = context.Request.Method, path, query, headers });
        });

        await application.StartAsync();
        var address = new Uri(application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

        return new StubDestination(name, application, seen, address);
    }

    public async ValueTask DisposeAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
    }

    private static async Task EchoAsync(WebSocket socket, string name, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                break;
            }

            var reply = Encoding.UTF8.GetBytes($"{name}:{Encoding.UTF8.GetString(buffer, 0, result.Count)}");
            await socket.SendAsync(reply, WebSocketMessageType.Text, true, cancellationToken);
        }
    }
}
