using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// The kiosk reaches the layout hub cross-origin. The hub path, and only the hub path, answers a browser's preflight for the
/// configured origins: the preflight carries no token, so it must be answered before authentication refuses it.
/// </summary>
[Trait("Category", "FixtureLogic")]
public class LayoutHubCorsTests
{
    private const string AllowedOrigin = "http://localhost:5180";
    private const string NegotiatePath = LayoutLifecycleHub.Path + "/negotiate";
    private const string RestPath = "/layouts";
    private const string RequestedHeaders = "authorization,content-type,x-requested-with,x-signalr-user-agent";

    [Fact]
    public async Task A_token_less_preflight_from_an_allowed_origin_is_answered_with_the_policy()
    {
        await using var host = await CorsHost.StartAsync(AllowedOrigin);

        var response = await host.PreflightAsync(NegotiatePath, AllowedOrigin);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AllowedOrigin]);
        response.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
        response.Headers.GetValues("Access-Control-Max-Age").ShouldBe(["600"]);
        var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant();
        foreach (var header in RequestedHeaders.Split(','))
        {
            allowedHeaders.ShouldContain(header);
        }

        var allowedMethods = response.Headers.GetValues("Access-Control-Allow-Methods").Single();
        foreach (var method in new[] { "GET", "POST", "DELETE" })
        {
            allowedMethods.ShouldContain(method);
        }
    }

    [Fact]
    public async Task A_preflight_from_an_origin_that_is_not_configured_gets_no_allow_origin()
    {
        await using var host = await CorsHost.StartAsync(AllowedOrigin);

        var response = await host.PreflightAsync(NegotiatePath, "http://fab.example");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task A_preflight_against_a_rest_path_gets_no_cors_headers()
    {
        await using var host = await CorsHost.StartAsync(AllowedOrigin);

        var response = await host.PreflightAsync(RestPath, AllowedOrigin);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    [Fact]
    public async Task An_actual_request_to_the_hub_path_from_an_allowed_origin_carries_the_echoed_origin_and_credentials()
    {
        await using var host = await CorsHost.StartAsync(AllowedOrigin);
        using var request = new HttpRequestMessage(HttpMethod.Get, LayoutLifecycleHub.Path);
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await host.Client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([AllowedOrigin]);
        response.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.fab.example")]
    public async Task A_wildcard_origin_in_the_configuration_refuses_to_start_naming_the_key(string origin)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var host = await CorsHost.StartAsync(origin);
        });

        exception.Message.ShouldContain("LayoutHub:Cors:AllowedOrigins");
    }

    [Fact]
    public async Task Without_a_layout_hub_section_it_starts_and_answers_no_cors_headers()
    {
        await using var host = await CorsHost.StartAsync();

        var response = await host.PreflightAsync(NegotiatePath, AllowedOrigin);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    // The real host needs Postgres and the message broker to start, and the host-capture mechanism stops before the pipeline is built,
    // so the order the served host registers its middleware in is read from the entry point itself.
    [Fact]
    public void The_layout_composition_host_answers_the_hub_preflight_before_authentication_and_authorization()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "LayoutComposition", "Api", "Program.cs"));

        var cors = program.IndexOf("app.UseLayoutHubCors();", StringComparison.Ordinal);
        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authorization = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        cors.ShouldBeGreaterThanOrEqualTo(0);
        authentication.ShouldBeGreaterThan(cors);
        authorization.ShouldBeGreaterThan(cors);
        program.ShouldContain("builder.AddLayoutHubCors();");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    private sealed class CorsHost : IAsyncDisposable
    {
        private readonly WebApplication application;

        private CorsHost(WebApplication application, HttpClient client)
        {
            this.application = application;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<CorsHost> StartAsync(string? allowedOrigin = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            if (allowedOrigin is not null)
            {
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["LayoutHub:Cors:AllowedOrigins:0"] = allowedOrigin });
            }

            builder.AddLayoutHubCors();
            builder.Services
                .AddAuthentication("deny")
                .AddScheme<AuthenticationSchemeOptions, DenyHandler>("deny", _ => { });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseLayoutHubCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapMethods(NegotiatePath, ["GET", "POST"], () => Results.Ok()).RequireAuthorization();
            app.MapGet(LayoutLifecycleHub.Path, () => Results.Ok());
            app.MapGet(RestPath, () => Results.Ok());
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new CorsHost(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        public Task<HttpResponseMessage> PreflightAsync(string path, string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, path);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", RequestedHeaders);
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
        }
    }

    private sealed class DenyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
