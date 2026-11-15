using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace SmartSentinelEye.Integration.Tests.ApiGateway;

/// <summary>
/// In <c>Ui:Mode=Proxy</c> the page comes from a dev server, so the gateway stamps the development policy on it, replacing whatever the
/// dev server sent; REST answers stay untouched, and without <c>Ui:Mode</c> the gateway adds none of these headers.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class GatewaySecurityHeadersProxyTests
{
    private const string DevelopmentPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self' http://identity.test http://hub.test:5000 ws://hub.test:5000 http://media.test:8889; "
        + "object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'self'";

    [Fact]
    public async Task Gateway_replaces_the_dev_servers_policy_with_the_development_policy_on_the_shell_page()
    {
        await using var stack = await HtmlProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([DevelopmentPolicy]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["SAMEORIGIN"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["same-origin"]);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Gateway_leaves_a_rest_answer_without_a_policy_in_proxy_mode()
    {
        await using var stack = await HtmlProxyStack.StartAsync();

        var response = await stack.Gateway.Client.GetAsync("/api/camera-catalog/x");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
    }

    [Fact]
    public async Task Gateway_adds_none_of_these_headers_when_no_ui_mode_is_set()
    {
        await using var rest = await StubDestination.StartAsync("rest");
        await using var gateway = await GatewayTestHost.StartAsync(overrides => GatewayTestHost.PointContextsAt(overrides, rest.Address));

        var response = await gateway.Client.GetAsync("/api/camera-catalog/x");

        response.Headers.Contains("Content-Security-Policy").ShouldBeFalse();
        response.Headers.Contains("X-Frame-Options").ShouldBeFalse();
        response.Headers.Contains("Referrer-Policy").ShouldBeFalse();
        response.Headers.Contains("Permissions-Policy").ShouldBeFalse();
    }

    private sealed class HtmlProxyStack : IAsyncDisposable
    {
        private readonly HtmlStub html;
        private readonly StubDestination rest;

        private HtmlProxyStack(HtmlStub html, StubDestination rest, GatewayTestHost gateway)
        {
            this.html = html;
            this.rest = rest;
            Gateway = gateway;
        }

        public GatewayTestHost Gateway { get; }

        public static async Task<HtmlProxyStack> StartAsync()
        {
            var html = await HtmlStub.StartAsync();
            var rest = await StubDestination.StartAsync("rest");
            var gateway = await GatewayTestHost.StartAsync(overrides =>
            {
                GatewayTestHost.PointContextsAt(overrides, rest.Address);
                overrides["Ui:Mode"] = "Proxy";
                overrides["Ui:Proxy:Shell"] = html.Address.ToString();
                overrides["Ui:Proxy:Cameras"] = html.Address.ToString();
                overrides["Ui:Proxy:EventIngestion"] = html.Address.ToString();
                overrides["Ui:Proxy:Kiosk"] = html.Address.ToString();
                overrides["Ui:Proxy:Wall"] = html.Address.ToString();
                overrides["Ui:ContentSecurityPolicy:IdentityProvider"] = "http://identity.test";
                overrides["Ui:ContentSecurityPolicy:LayoutHub"] = "http://hub.test:5000";
                overrides["Ui:ContentSecurityPolicy:Media"] = "http://media.test:8889";
            });

            return new HtmlProxyStack(html, rest, gateway);
        }

        public async ValueTask DisposeAsync()
        {
            await Gateway.DisposeAsync();
            await html.DisposeAsync();
            await rest.DisposeAsync();
        }
    }

    /// <summary>A dev server that answers every request with an HTML page carrying a policy of its own.</summary>
    private sealed class HtmlStub : IAsyncDisposable
    {
        private readonly WebApplication application;

        private HtmlStub(WebApplication application, Uri address)
        {
            this.application = application;
            Address = address;
        }

        public Uri Address { get; }

        public static async Task<HtmlStub> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var application = builder.Build();
            application.Run(async context =>
            {
                context.Response.ContentType = "text/html";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
                await context.Response.WriteAsync("<html>dev-shell</html>");
            });

            await application.StartAsync();
            var address = new Uri(application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

            return new HtmlStub(application, address);
        }

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }
}
