using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 440 FR-004b / AS-27 (T163, T164) — the migrator tells the operator to check the
/// key pair only when the <b>token mint</b> refused. That distinction has to be made where
/// it is known: a 400/401 from the admin API itself (a valid, freshly minted bearer that
/// the API still refuses) is a different fault and must reach the caller as it is, not
/// dressed as a credential problem.
///
/// <para>
/// Both halves go through the real registration, because the claim is about which of two
/// HTTP exchanges failed.
/// </para>
/// </summary>
public sealed class KeycloakAdminTokenRefusalTests
{
    private const string Authority = "https://keycloak.test/";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refused_assertion_mint_surfaces_as_the_typed_refusal_carrying_the_status(HttpStatusCode status)
    {
        using var provider = ProviderOver(
            new Stub(_ => new HttpResponseMessage(status)),
            new KeycloakAdminOptions
            {
                BaseUrl = Authority,
                AdminClientId = "migration-runner",
                AdminClientPrivateKey = NewPrivateKeyBase64(),
            });

        var thrown = await Should.ThrowAsync<KeycloakAdminTokenRefusedException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        thrown.StatusCode.ShouldBe(status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_mint_refused_inside_an_admin_call_reaches_the_caller_as_the_typed_refusal(HttpStatusCode status)
    {
        Stub token = new(_ => new HttpResponseMessage(status));
        Stub admin = new(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await using var services = AdminClientServices(token, admin);
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();

        var thrown = await Should.ThrowAsync<KeycloakAdminTokenRefusedException>(
            () => client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.StatusCode.ShouldBe(status);
        admin.Count.ShouldBe(0, "a refused mint means the admin API is never asked.");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refusal_from_the_admin_API_itself_is_not_the_typed_mint_refusal(HttpStatusCode status)
    {
        Stub token = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"token-1","expires_in":300,"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json"),
        });
        Stub admin = new(_ => new HttpResponseMessage(status));

        await using var services = AdminClientServices(token, admin);
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();

        var thrown = await Should.ThrowAsync<HttpRequestException>(
            () => client.RemoveRealmDefaultPrivilegeAsync("offline_access", CancellationToken.None));

        thrown.ShouldNotBeOfType<KeycloakAdminTokenRefusedException>(
            "the mint succeeded; telling the operator to check the key pair would send them to the wrong place.");
        thrown.StatusCode.ShouldBe(status);
        token.Count.ShouldBe(1);
    }

    private static ServiceProvider AdminClientServices(Stub token, Stub admin)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.AddServiceDefaults();
        builder.Configuration["ConnectionStrings:keycloak"] = Authority;
        builder.Configuration["Keycloak:AdminClientId"] = "migration-runner";
        builder.Configuration["Keycloak:AdminClientPrivateKey"] = NewPrivateKeyBase64();
        builder.AddKeycloakAdminClient();

        builder.Services.AddHttpClient(KeycloakAdminTokenProvider.AssertionHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => token);
        builder.Services.AddHttpClient<HttpKeycloakAdminClient>()
            .ConfigurePrimaryHttpMessageHandler(() => admin);

        // Retry without waiting: GETs are retried by the default pipeline and the claim is about
        // the exception that finally surfaces, not about the delay before it.
        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        return builder.Services.BuildServiceProvider();
    }

    private static KeycloakAdminTokenProvider ProviderOver(Stub token, KeycloakAdminOptions options) =>
        new(
            new SingleClientFactory(new HttpClient(token)),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<KeycloakAdminTokenProvider>.Instance);

    private static string NewPrivateKeyBase64()
    {
        using var key = RSA.Create(2048);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int count;

        public int Count => count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(respond(request));
        }
    }
}
