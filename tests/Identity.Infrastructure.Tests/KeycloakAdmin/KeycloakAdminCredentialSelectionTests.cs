using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.KeycloakAdmin;

/// <summary>
/// Spec 440 T-E (ADR-0186, FR-010, FR-011) — which credential the Keycloak Admin token
/// mint presents, decided by configuration, and over which named client.
///
/// <para>
/// The secret path is <b>characterisation</b>: green before and after, because five
/// other service accounts and Identity itself still authenticate by secret and must not
/// move. The rest is new behaviour and red until the provider selects.
/// </para>
/// </summary>
public sealed class KeycloakAdminCredentialSelectionTests
{
    private const string Authority = "https://keycloak.test/";

    private const string TokenUrl = "https://keycloak.test/realms/smart-sentinel-eye/protocol/openid-connect/token";

    private const string AssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    // ---- characterisation: the secret path, byte-for-byte as it is today ----

    [Fact]
    public async Task A_configured_secret_mints_with_the_secret_form_on_the_existing_named_client()
    {
        TokenEndpoint keycloak = new();
        RecordingFactory factory = new(new HttpClient(keycloak));

        using var provider = ProviderOver(factory, new KeycloakAdminOptions
        {
            BaseUrl = Authority,
            AdminClientId = "identity-admin",
            AdminClientSecret = "a-secret",
        });
        await provider.GetAccessTokenAsync(CancellationToken.None);

        var form = HttpUtility.ParseQueryString(keycloak.Forms.Single());
        form["grant_type"].ShouldBe("client_credentials");
        form["client_id"].ShouldBe("identity-admin");
        form["client_secret"].ShouldBe("a-secret");
        form.AllKeys.ShouldNotContain("client_assertion");
        factory.RequestedNames.ShouldBe([KeycloakAdminTokenProvider.HttpClientName]);
        keycloak.Urls.ShouldBe([TokenUrl]);
    }

    // ---- red: the assertion path ----

    [Fact]
    public async Task A_configured_private_key_mints_with_an_assertion_on_its_own_named_client()
    {
        TokenEndpoint keycloak = new();
        RecordingFactory factory = new(new HttpClient(keycloak));

        using var provider = ProviderOver(factory, new KeycloakAdminOptions
        {
            BaseUrl = Authority,
            AdminClientId = "migration-runner",
            AdminClientPrivateKey = NewPrivateKeyBase64(),
        });
        await provider.GetAccessTokenAsync(CancellationToken.None);

        var form = HttpUtility.ParseQueryString(keycloak.Forms.Single());
        form["grant_type"].ShouldBe("client_credentials");
        form["client_id"].ShouldBe("migration-runner");
        form["client_assertion_type"].ShouldBe(AssertionType);
        form["client_assertion"].ShouldNotBeNullOrWhiteSpace();
        form.AllKeys.ShouldNotContain("client_secret");
        factory.RequestedNames.ShouldBe(
            [KeycloakAdminTokenProvider.AssertionHttpClientName],
            "the assertion mint must not share the secret mint's client: that one retries POSTs, and a retried assertion is a replayed jti.");
    }

    [Fact]
    public async Task Both_a_secret_and_a_private_key_is_refused_at_the_first_mint_naming_both_settings()
    {
        TokenEndpoint keycloak = new();
        using var provider = ProviderOver(
            new RecordingFactory(new HttpClient(keycloak)),
            new KeycloakAdminOptions
            {
                BaseUrl = Authority,
                AdminClientId = "migration-runner",
                AdminClientSecret = "a-secret",
                AdminClientPrivateKey = NewPrivateKeyBase64(),
            });

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        thrown.Message.ShouldContain("Keycloak:AdminClientSecret");
        thrown.Message.ShouldContain("Keycloak:AdminClientPrivateKey");
        keycloak.Forms.ShouldBeEmpty("an ambiguous credential must not be resolved by trying one of them.");
    }

    /// <summary>
    /// Spec 440 FR-011 / AS-26, red until the provider refuses an absent credential: today
    /// "neither" falls through to the secret path and posts an empty <c>client_secret</c>,
    /// which Keycloak answers 401 — a misconfiguration reported as a bad credential.
    /// </summary>
    [Fact]
    public async Task Neither_a_secret_nor_a_private_key_is_refused_naming_both_settings_and_sends_nothing()
    {
        TokenEndpoint keycloak = new();
        using var provider = ProviderOver(
            new RecordingFactory(new HttpClient(keycloak)),
            new KeycloakAdminOptions
            {
                BaseUrl = Authority,
                AdminClientId = "migration-runner",
                AdminClientSecret = string.Empty,
                AdminClientPrivateKey = string.Empty,
            });

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => provider.GetAccessTokenAsync(CancellationToken.None));

        thrown.Message.ShouldContain("Keycloak:AdminClientSecret");
        thrown.Message.ShouldContain("Keycloak:AdminClientPrivateKey");
        keycloak.Forms.ShouldBeEmpty("a missing credential must not be sent as an empty one.");
    }

    /// <summary>
    /// Through the real registration and the real defaults pipeline (the shape
    /// <c>RetryEveryMethodCompositionTests</c> uses), because the claim is about what the
    /// pipeline does to a POST that a Keycloak 503 answered: it must be sent once.
    /// </summary>
    [Fact]
    public async Task The_assertion_mint_is_attempted_once_when_Keycloak_answers_503()
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.AddServiceDefaults();
        builder.Configuration["ConnectionStrings:keycloak"] = Authority;
        builder.Configuration["Keycloak:AdminClientId"] = "migration-runner";
        builder.Configuration["Keycloak:AdminClientPrivateKey"] = NewPrivateKeyBase64();
        builder.AddKeycloakAdminClient();

        TokenEndpoint keycloak = new() { Status = HttpStatusCode.ServiceUnavailable };
        foreach (var name in new[]
        {
            KeycloakAdminTokenProvider.AssertionHttpClientName,
            KeycloakAdminTokenProvider.HttpClientName,
        })
        {
            builder.Services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => keycloak);
        }

        builder.Services.Configure<HttpStandardResilienceOptions>("-standard", options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        using var provider = builder.Services.BuildServiceProvider();
        var tokens = provider.GetRequiredService<KeycloakAdminTokenProvider>();

        await Should.ThrowAsync<HttpRequestException>(
            () => tokens.GetAccessTokenAsync(CancellationToken.None));

        keycloak.Forms.Count.ShouldBe(
            1,
            "the pipeline resends identical content, i.e. the same jti, which Keycloak rejects as a replay; "
            + "the assertion client must therefore not opt in to RetryEveryMethod().");
    }

    private static KeycloakAdminTokenProvider ProviderOver(IHttpClientFactory factory, KeycloakAdminOptions options) =>
        new(factory, Options.Create(options), TimeProvider.System, NullLogger<KeycloakAdminTokenProvider>.Instance);

    private static string NewPrivateKeyBase64()
    {
        using var key = RSA.Create(2048);
        return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
    }

    private sealed class RecordingFactory(HttpClient client) : IHttpClientFactory
    {
        public List<string> RequestedNames { get; } = [];

        public HttpClient CreateClient(string name)
        {
            RequestedNames.Add(name);
            return client;
        }
    }

    private sealed class TokenEndpoint : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public List<string> Forms { get; } = [];

        public List<string> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.GetLeftPart(UriPartial.Path));
            Forms.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return Status != HttpStatusCode.OK
                ? new HttpResponseMessage(Status)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"token-1","expires_in":300,"token_type":"Bearer"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
        }
    }
}
