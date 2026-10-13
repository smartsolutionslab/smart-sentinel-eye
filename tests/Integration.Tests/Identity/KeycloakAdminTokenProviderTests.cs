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
    private readonly AspireFixture fixture;

    public KeycloakAdminTokenProviderTests(AspireFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Client_credentials_grant_against_identity_admin_returns_a_usable_token()
    {
        var provider = CreateProvider();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        var token = await provider.GetAccessTokenAsync(cts.Token);

        token.ShouldNotBeNullOrWhiteSpace();
        token.Split('.').Length.ShouldBe(3, "Keycloak emits JWS access tokens (header.payload.signature).");
    }

    [Fact]
    public async Task Second_call_within_token_lifetime_returns_the_cached_token()
    {
        var provider = CreateProvider();
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        var first = await provider.GetAccessTokenAsync(cts.Token);
        var second = await provider.GetAccessTokenAsync(cts.Token);

        second.ShouldBe(first);
    }

    private KeycloakAdminTokenProvider CreateProvider()
    {
        var http = fixture.CreateKeycloakClient();
        KeycloakAdminOptions options = new()
        {
            BaseUrl = http.BaseAddress!.ToString(),
            Realm = RealmProbe.Realm,
            AdminClientId = RealmProbe.AdminClientId,
            AdminClientSecret = RealmProbe.AdminClientSecret,
        };
        return new KeycloakAdminTokenProvider(
            new FakeHttpClientFactory(http),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<KeycloakAdminTokenProvider>.Instance);
    }
}
