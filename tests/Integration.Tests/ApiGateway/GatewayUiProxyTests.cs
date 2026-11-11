using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// Spec 491 (#3245), T006, User Story 2. With <c>Ui:Mode=Proxy</c> the gateway fronts the three Vite dev servers: the shell at
/// <c>/</c>, the remotes under <c>/remotes/&lt;name&gt;/</c>. A remote's path is forwarded unchanged because the dev server's Vite
/// <c>base</c> expects the prefix (plan D4/D6); the hot-reload WebSocket is carried by the proxy; the pages are not rate limited
/// (plan D5). With no <c>Ui</c> section the gateway keeps its "no accidental catch-all" 404.
/// </summary>
public class GatewayUiProxyTests
{
    [Fact]
    public async Task Gateway_answers_the_root_and_an_unmatched_page_path_from_the_shell()
    {
        await using var stack = await UiProxyStack.StartAsync();

        var root = await ReadEchoAsync(stack.Gateway.Client, "/");
        var deepLink = await ReadEchoAsync(stack.Gateway.Client, "/layouts/123");

        root.Name.ShouldBe("shell");
        root.Path.ShouldBe("/");
        deepLink.Name.ShouldBe("shell");
        deepLink.Path.ShouldBe("/layouts/123");
    }

    [Theory]
    [InlineData("/remotes/cameras/remoteEntry.js", "cameras")]
    [InlineData("/remotes/cameras/src/main.tsx", "cameras")]
    [InlineData("/remotes/event-ingestion/remoteEntry.js", "event-ingestion")]
    [InlineData("/remotes/event-ingestion/@vite/client", "event-ingestion")]
    public async Task Gateway_sends_a_remote_path_to_its_dev_server_with_the_prefix_unchanged(string path, string expectedDestination)
    {
        await using var stack = await UiProxyStack.StartAsync();

        var echo = await ReadEchoAsync(stack.Gateway.Client, path);

        echo.Name.ShouldBe(expectedDestination);
        echo.Path.ShouldBe(path);
    }

    [Theory]
    [InlineData("/?token=abc", "shell")]
    [InlineData("/remotes/cameras/?token=abc", "cameras")]
    public async Task Gateway_carries_a_hot_reload_websocket_to_the_dev_server_that_owns_the_path(string pathAndQuery, string expectedDestination)
    {
        await using var stack = await UiProxyStack.StartAsync();
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("vite-hmr");
        var address = new UriBuilder(stack.Gateway.Address) { Scheme = "ws" }.Uri;

        await socket.ConnectAsync(new Uri(address, pathAndQuery), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[256];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        socket.SubProtocol.ShouldBe("vite-hmr");
        Encoding.UTF8.GetString(buffer, 0, result.Count).ShouldBe($"{expectedDestination}:ping");
    }

    [Fact]
    public async Task Gateway_does_not_rate_limit_the_proxied_pages()
    {
        await using var stack = await UiProxyStack.StartAsync(overrides => overrides["RateLimiting:PermitLimit"] = "2");

        List<HttpStatusCode> statuses = [];
        for (var attempt = 0; attempt < 8; attempt++)
        {
            statuses.Add((await stack.Gateway.Client.GetAsync("/")).StatusCode);
            statuses.Add((await stack.Gateway.Client.GetAsync("/remotes/cameras/remoteEntry.js")).StatusCode);
        }

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gateway_answers_an_unknown_remote_with_404_rather_than_the_shell()
    {
        await using var stack = await UiProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/remotes/not-a-remote/remoteEntry.js");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Gateway_keeps_the_404_for_an_unrouted_path_when_no_ui_is_configured()
    {
        await using var shell = await StubDestination.StartAsync("shell");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, shell.Address));

        var response = await gateway.Client.GetAsync("/layouts/123");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        shell.Requests.ShouldBeEmpty();
    }

    private static async Task<Echo> ReadEchoAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return new Echo(document.RootElement.GetProperty("name").GetString()!, document.RootElement.GetProperty("path").GetString()!);
    }

    private sealed record Echo(string Name, string Path);

    /// <summary>Three stub dev servers behind a gateway running in <c>Ui:Mode=Proxy</c>.</summary>
    private sealed class UiProxyStack : IAsyncDisposable
    {
        private UiProxyStack(StubDestination shell, StubDestination cameras, StubDestination eventIngestion, StubDestination rest, GatewayTestHost gateway)
        {
            Shell = shell;
            Cameras = cameras;
            EventIngestion = eventIngestion;
            Rest = rest;
            Gateway = gateway;
        }

        public StubDestination Shell { get; }

        public StubDestination Cameras { get; }

        public StubDestination EventIngestion { get; }

        public StubDestination Rest { get; }

        public GatewayTestHost Gateway { get; }

        public static async Task<UiProxyStack> StartAsync(Action<Dictionary<string, string?>>? configure = null)
        {
            var shell = await StubDestination.StartAsync("shell");
            var cameras = await StubDestination.StartAsync("cameras");
            var eventIngestion = await StubDestination.StartAsync("event-ingestion");
            var rest = await StubDestination.StartAsync("rest");
            var gateway = await GatewayTestHost.StartAsync(overrides =>
            {
                GatewayTestHost.PointContextsAt(overrides, rest.Address);
                overrides["Ui:Mode"] = "Proxy";
                overrides["Ui:Proxy:Shell"] = shell.Address.ToString();
                overrides["Ui:Proxy:Cameras"] = cameras.Address.ToString();
                overrides["Ui:Proxy:EventIngestion"] = eventIngestion.Address.ToString();
                configure?.Invoke(overrides);
            });

            return new UiProxyStack(shell, cameras, eventIngestion, rest, gateway);
        }

        public async ValueTask DisposeAsync()
        {
            await Gateway.DisposeAsync();
            await Shell.DisposeAsync();
            await Cameras.DisposeAsync();
            await EventIngestion.DisposeAsync();
            await Rest.DisposeAsync();
        }
    }
}
