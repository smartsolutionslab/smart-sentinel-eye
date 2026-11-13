using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// With <c>Ui:Mode=Proxy</c> the kiosk and the wall display are paths on the gateway's origin, <c>/kiosk/</c> and <c>/wall/</c>,
/// forwarded unchanged to their Vite dev servers ahead of the shell's catch-all. Both instances are required configuration.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewayKioskUiProxyTests
{
    [Theory]
    [InlineData("/kiosk/", "kiosk")]
    [InlineData("/kiosk/layouts/abc", "kiosk")]
    [InlineData("/kiosk/src/main.tsx", "kiosk")]
    [InlineData("/wall/", "wall")]
    [InlineData("/wall/walls/x", "wall")]
    [InlineData("/wall/@vite/client", "wall")]
    public async Task Gateway_sends_a_kiosk_or_wall_path_to_its_dev_server_with_the_path_unchanged(string path, string expectedDestination)
    {
        await using var stack = await UiProxyStack.StartAsync();

        var echo = await ReadEchoAsync(stack.Gateway.Client, path);

        echo.Name.ShouldBe(expectedDestination);
        echo.Path.ShouldBe(path);
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/kiosk", "/kiosk/")]
    [InlineData("/wall", "/wall/")]
    public async Task Gateway_redirects_the_bare_prefix_to_the_slashed_path(string path, string expectedLocation)
    {
        await using var stack = await UiProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(path);

        ((int)response.StatusCode).ShouldBeInRange(300, 399);
        response.Headers.Location!.OriginalString.ShouldBe(expectedLocation);
        stack.Kiosk.Requests.ShouldBeEmpty();
        stack.Wall.Requests.ShouldBeEmpty();
        stack.Shell.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/kiosk?x=1", "/kiosk/?x=1")]
    [InlineData("/wall?x=1", "/wall/?x=1")]
    public async Task Gateway_keeps_the_query_when_it_redirects_the_bare_prefix(string pathAndQuery, string expectedLocation)
    {
        await using var stack = await UiProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync(pathAndQuery);

        ((int)response.StatusCode).ShouldBeInRange(300, 399);
        response.Headers.Location!.OriginalString.ShouldBe(expectedLocation);
    }

    [Fact]
    public async Task Gateway_does_not_rate_limit_or_cors_the_kiosk_and_wall_routes()
    {
        await using var stack = await UiProxyStack.StartAsync(overrides =>
        {
            overrides["RateLimiting:PermitLimit"] = "2";
            overrides["Cors:AllowedOrigins:0"] = "http://localhost:5174";
        });

        List<HttpStatusCode> statuses = [];
        for (var attempt = 0; attempt < 8; attempt++)
        {
            statuses.Add((await stack.Gateway.Client.GetAsync("/kiosk/")).StatusCode);
            statuses.Add((await stack.Gateway.Client.GetAsync("/wall/")).StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/kiosk/");
        request.Headers.Add("Origin", "http://localhost:5174");
        var withOrigin = await stack.Gateway.Client.SendAsync(request);

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
        withOrigin.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/kiosk/?token=abc", "kiosk")]
    [InlineData("/wall/?token=abc", "wall")]
    public async Task Gateway_carries_a_hot_reload_websocket_to_the_kiosk_or_wall_dev_server(string pathAndQuery, string expectedDestination)
    {
        await using var stack = await UiProxyStack.StartAsync();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("vite-hmr");
        var address = new UriBuilder(stack.Gateway.Address) { Scheme = "ws" }.Uri;

        await socket.ConnectAsync(new Uri(address, pathAndQuery), deadline.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, deadline.Token);
        var buffer = new byte[256];
        var result = await socket.ReceiveAsync(buffer, deadline.Token);

        Encoding.UTF8.GetString(buffer, 0, result.Count).ShouldBe($"{expectedDestination}:ping");
        var destination = expectedDestination == "kiosk" ? stack.Kiosk : stack.Wall;
        destination.Requests.Select(request => request.Query).ShouldContain("?token=abc");
    }

    [Theory]
    [InlineData("Ui:Proxy:Kiosk")]
    [InlineData("Ui:Proxy:Wall")]
    public async Task Gateway_refuses_to_start_in_proxy_mode_when_an_instance_is_not_configured(string configurationKey)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            UiProxyStack.StartAsync(overrides => overrides.Remove(configurationKey)));

        exception.Message.ShouldContain(configurationKey);
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
}
