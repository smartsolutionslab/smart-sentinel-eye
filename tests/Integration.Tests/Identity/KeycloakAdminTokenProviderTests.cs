using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Verifies the <see cref="KeycloakAdminTokenProvider"/> against the
/// real Keycloak booted by the AspireFixture: the
/// <c>identity-admin</c> service-account client (seeded by the realm
/// import in PR E) issues a usable access token via the
/// client_credentials grant, and the provider caches the token
/// across calls within the lifetime.
/// </summary>
[Collection(AspireCollection.Name)]
public class KeycloakAdminTokenProviderTests
{
    private const string AdminClientId = "identity-admin";
    private const string AdminClientSecret = "dev-only-identity-admin-secret";
    private const string Realm = "smart-sentinel-eye";

    private readonly AspireFixture _fixture;

    public KeycloakAdminTokenProviderTests(AspireFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Client_credentials_grant_against_identity_admin_returns_a_usable_token()
    {
        KeycloakAdminTokenProvider provider = CreateProvider();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        string token = await provider.GetAccessTokenAsync(cts.Token);

        token.ShouldNotBeNullOrWhiteSpace();
        token.Split('.').Length.ShouldBe(3, "Keycloak emits JWS access tokens (header.payload.signature).");
    }

    [Fact]
    public async Task Second_call_within_token_lifetime_returns_the_cached_token()
    {
        KeycloakAdminTokenProvider provider = CreateProvider();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        string first = await provider.GetAccessTokenAsync(cts.Token);
        string second = await provider.GetAccessTokenAsync(cts.Token);

        second.ShouldBe(first);
    }

    private KeycloakAdminTokenProvider CreateProvider()
    {
        HttpClient http = _fixture.App.CreateHttpClient("keycloak");
        KeycloakAdminOptions options = new()
        {
            BaseUrl = http.BaseAddress!.ToString(),
            Realm = Realm,
            AdminClientId = AdminClientId,
            AdminClientSecret = AdminClientSecret,
        };
        return new KeycloakAdminTokenProvider(
            http,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<KeycloakAdminTokenProvider>.Instance);
    }
}
