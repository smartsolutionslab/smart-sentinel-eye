using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults.Resilience;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Characterisation (spec 369, ADR-0144), captured green on unmodified
/// <c>AddRevocationCheck</c> before it is split. Pins what the revocation half
/// of <c>AddBearerAuthentication</c> composes: which registrations yield to a
/// pre-registered one (<c>TryAdd</c>), the options it fills in, the two named
/// clients and their retry opt-in (ADR-0143), and the health check (ADR-0154).
///
/// <para>
/// Every expected value is a literal written here. An empty builder is used so
/// no <c>appsettings.json</c> or ambient variable can supply a value. Resilience
/// defaults are registered before the call the way <c>AddServiceDefaults</c>
/// does, with the delay set to zero and nothing else: the attempt count is what
/// is asserted.
/// </para>
/// </summary>
public class RevocationCompositionCharacterisationTests
{
    [Fact]
    public void A_pre_registered_time_provider_wins()
    {
        TimeProvider preRegistered = new FixedTimeProvider();

        using var provider = Compose(pre: services => services.AddSingleton(preRegistered));

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(preRegistered);
    }

    [Fact]
    public void A_pre_registered_revoked_client_registry_class_wins()
    {
        RevokedClientRegistry registry = new();

        using var provider = Compose(pre: services => services.AddSingleton(registry));

        provider.GetRequiredService<RevokedClientRegistry>().ShouldBeSameAs(registry);
    }

    [Fact]
    public void A_pre_registered_registry_interface_wins()
    {
        StubRegistry registry = new();

        using var provider = Compose(pre: services => services.AddSingleton<IRevokedClientRegistry>(registry));

        provider.GetRequiredService<IRevokedClientRegistry>().ShouldBeSameAs(registry);
    }

    [Fact]
    public void A_pre_registered_source_wins()
    {
        StubSource source = new();

        using var provider = Compose(pre: services => services.AddSingleton<IRevokedClientSource>(source));

        provider.GetRequiredService<IRevokedClientSource>().ShouldBeSameAs(source);
    }

    [Fact]
    public void A_pre_registered_token_provider_wins()
    {
        using var preBuilt = Compose();
        var tokenProvider = preBuilt.GetRequiredService<RevocationListTokenProvider>();

        using var provider = Compose(pre: services => services.AddSingleton(tokenProvider));

        provider.GetRequiredService<RevocationListTokenProvider>().ShouldBeSameAs(tokenProvider);
    }

    [Fact]
    public void A_pre_registered_refresher_wins()
    {
        using var preBuilt = Compose();
        var refresher = preBuilt.GetRequiredService<RevokedClientRefresher>();

        using var provider = Compose(pre: services => services.AddSingleton(refresher));

        provider.GetRequiredService<RevokedClientRefresher>().ShouldBeSameAs(refresher);
        provider.GetServices<IHostedService>().OfType<RevokedClientRefresher>().ShouldHaveSingleItem()
            .ShouldBeSameAs(refresher);
    }

    [Fact]
    public void Without_a_pre_registration_the_registry_interface_resolves_to_the_registry_class()
    {
        using var provider = Compose();

        provider.GetRequiredService<IRevokedClientRegistry>()
            .ShouldBeSameAs(provider.GetRequiredService<RevokedClientRegistry>());
    }

    [Fact]
    public void Without_a_pre_registration_the_source_is_the_http_one()
    {
        using var provider = Compose();

        provider.GetRequiredService<IRevokedClientSource>().ShouldBeOfType<HttpRevokedClientSource>();
    }

    [Fact]
    public void The_options_take_the_untrimmed_base_url_and_the_realm_over_configuration_and_bind_the_credential()
    {
        using var provider = Compose(
            settings:
            [
                ("RevocationList:KeycloakUrl", "https://from-configuration.invalid"),
                ("RevocationList:Realm", "from-configuration"),
                ("RevocationList:ClientId", "reader-from-configuration"),
                ("RevocationList:ClientSecret", "secret-from-configuration"),
            ],
            keycloakUrl: "https://kc.invalid/");

        var options = provider.GetRequiredService<IOptions<RevocationListOptions>>().Value;

        options.KeycloakUrl.ShouldBe("https://kc.invalid/");
        options.Realm.ShouldBe("smart-sentinel-eye");
        options.ClientId.ShouldBe("reader-from-configuration");
        options.ClientSecret.ShouldBe("secret-from-configuration");
    }

    [Fact]
    public void A_custom_realm_reaches_the_options()
    {
        var builder = EmptyBuilder(
            "https://kc.invalid",
            [("RevocationList:ClientId", "reader"), ("RevocationList:ClientSecret", "secret")]);
        builder.AddBearerAuthentication(realm: "other-realm");
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RevocationListOptions>>().Value.Realm.ShouldBe("other-realm");
    }

    [Fact]
    public void A_missing_secret_with_the_http_source_fails_validation_with_todays_message()
    {
        using var provider = Compose(settings: []);

        var thrown = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RevocationListOptions>>().Value);

        thrown.Failures.ShouldBe(
        [
            "RevocationList:ClientId and RevocationList:ClientSecret must both be set for the "
            + "HTTP revocation-list source.",
        ]);
    }

    [Fact]
    public void The_fetch_client_is_based_on_the_identity_resource()
    {
        using var provider = Compose();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("revocation-list-fetch");

        client.BaseAddress.ShouldBe(new Uri("http://identity/"));
    }

    [Fact]
    public async Task A_get_through_the_fetch_client_carries_a_bearer_token_minted_through_the_token_client()
    {
        RecordingHandler fetchServer = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        RecordingHandler tokenServer = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"minted-token","expires_in":300,"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json"),
        });
        using var provider = Compose(fetchServer: fetchServer, tokenServer: tokenServer);

        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("revocation-list-fetch")
            .GetAsync("/registered-clients/revoked", CancellationToken.None);

        fetchServer.Requests.ShouldHaveSingleItem().Authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "minted-token"));
        tokenServer.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task The_token_client_opts_in_to_retrying_post()
    {
        RecordingHandler tokenServer = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = Compose(tokenServer: tokenServer);

        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("revocation-list-token")
            .PostAsync("https://kc.invalid/token", new StringContent("{}"), CancellationToken.None);

        tokenServer.Attempts.ShouldBe(4, "the token mint opts back in to retrying POST (ADR-0143).");
    }

    [Fact]
    public async Task The_fetch_client_does_not_opt_in_to_retrying_post()
    {
        RecordingHandler fetchServer = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        RecordingHandler tokenServer = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"minted-token","expires_in":300,"token_type":"Bearer"}""",
                Encoding.UTF8,
                "application/json"),
        });
        using var provider = Compose(fetchServer: fetchServer, tokenServer: tokenServer);

        using var response = await provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("revocation-list-fetch")
            .PostAsync("/anything", new StringContent("{}"), CancellationToken.None);

        fetchServer.Attempts.ShouldBe(1, "the fetch client has no retry opt-in, so a POST gets one attempt.");
    }

    [Fact]
    public void The_revocation_snapshot_health_check_is_registered_once_as_degraded_and_ready()
    {
        using var provider = Compose();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Where(candidate => candidate.Name == "revocation-snapshot")
            .ShouldHaveSingleItem();

        registration.FailureStatus.ShouldBe(HealthStatus.Degraded);
        registration.Tags.ShouldBe(["ready"]);
    }

    [Fact]
    public void The_revocation_snapshot_health_check_builds_the_snapshot_check()
    {
        using var provider = Compose();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(candidate => candidate.Name == "revocation-snapshot");

        registration.Factory(provider).ShouldBeOfType<RevokedClientSnapshotHealthCheck>();
    }

    private static ServiceProvider Compose(
        Action<IServiceCollection>? pre = null,
        (string Key, string Value)[]? settings = null,
        string keycloakUrl = "https://kc.invalid",
        RecordingHandler? fetchServer = null,
        RecordingHandler? tokenServer = null)
    {
        settings ??=
        [
            ("RevocationList:ClientId", "reader"),
            ("RevocationList:ClientSecret", "secret"),
        ];
        var builder = EmptyBuilder(keycloakUrl, settings);

        // The harness models per-client resilience, not the ConfigureHttpClientDefaults
        // pipeline production uses, so the retry tests pin the opt-in call
        // (RetryEveryMethod) and not a production guarantee about attempts.
        // The idempotent-only default, registered per client and before the call:
        // the order in which a default comes first and a client's own opt-in
        // (RetryEveryMethod) comes after it and wins. A ConfigureHttpClientDefaults
        // registration does not model that: it names its pipeline "-standard" for
        // every client, so a per-client opt-in never reaches it.
        foreach (var name in new[] { "revocation-list-token", "revocation-list-fetch" })
        {
            builder.Services.Configure<HttpStandardResilienceOptions>(
                $"{name}-standard",
                IdempotentRetry.RetryIdempotentMethodsOnly);
        }

        pre?.Invoke(builder.Services);

        builder.AddBearerAuthentication();

        AttachResilienceAndServer(builder.Services, "revocation-list-fetch", fetchServer);
        AttachResilienceAndServer(builder.Services, "revocation-list-token", tokenServer);

        return builder.Services.BuildServiceProvider();
    }

    private static void AttachResilienceAndServer(IServiceCollection services, string name, RecordingHandler? server)
    {
        var client = services.AddHttpClient(name);
        client.AddStandardResilienceHandler(options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        if (server is not null)
        {
            client.ConfigurePrimaryHttpMessageHandler(() => server);
        }
    }

    private static HostApplicationBuilder EmptyBuilder(string keycloakUrl, (string Key, string Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = keycloakUrl;

        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        return builder;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
    }

    private sealed class StubRegistry : IRevokedClientRegistry
    {
        public bool Refuses(string? azp, DateTimeOffset? issuedAt) => false;
    }

    private sealed class StubSource : IRevokedClientSource
    {
        public Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RevokedClientEntry>>([]);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<(AuthenticationHeaderValue? Authorization, HttpMethod Method)> seen = [];

        public int Attempts => seen.Count;

        public IReadOnlyList<(AuthenticationHeaderValue? Authorization, HttpMethod Method)> Requests => seen;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            seen.Add((request.Headers.Authorization, request.Method));
            return Task.FromResult(respond(request));
        }
    }
}
