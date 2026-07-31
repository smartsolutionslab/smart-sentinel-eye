using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin1234";
    public const string ClientId = "smart-sentinel-eye-web";

    // Token cache lives across all tests in the collection so a 295-test
    // run does not hammer Keycloak with a fresh password grant per test
    // (same reasoning as Yumney's AspireFixture).
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new();

    /// <summary>
    /// Every service declares <c>WithHttpEndpoint()</c> in AppHost and none
    /// declares an https one, but an ASP.NET project also carries an https
    /// launch profile — so leaving the choice to <c>CreateHttpClient</c>'s
    /// default made these clients depend on whichever endpoint that default
    /// preferred. Aspire 13.4.6 changed that preference to https and every
    /// request started failing with UntrustedRoot on CI, which has no dev cert.
    /// Naming the endpoint removes the ambient dependency (#1133).
    /// </summary>
    public HttpClient CreateServiceClient(string resourceName) =>
        App.CreateHttpClient(resourceName, "http");

    /// <summary>
    /// Keycloak is the exception: it exposes https only, so there is no http
    /// endpoint to name. It presents the ASP.NET dev certificate, which is
    /// trusted on a developer machine but not on CI — only the e2e job runs
    /// <c>dotnet dev-certs https</c>. Validating a self-signed dev cert on a
    /// throwaway container proves nothing, so this accepts it explicitly rather
    /// than depending on whether the host happens to trust it (#1133).
    /// </summary>
    public HttpClient CreateKeycloakClient()
    {
        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler) { BaseAddress = App.GetEndpoint("keycloak") };
    }

    public Task<HttpClient> CreateAdminClientAsync(string resourceName) =>
        CreateAuthenticatedClientAsync(resourceName, AdminUsername, AdminPassword);

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string resourceName, string username, string password)
    {
        string token = await GetAccessTokenAsync(username, password).ConfigureAwait(false);
        HttpClient client = CreateServiceClient(resourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> GetAccessTokenAsync(string username, string password)
    {
        string cacheKey = $"{username}|{password}";
        if (_tokenCache.TryGetValue(cacheKey, out CachedToken? cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow + ExpirySafetyMargin)
        {
            return cached.AccessToken;
        }

        CachedToken token = await FetchAccessTokenAsync(username, password).ConfigureAwait(false);
        _tokenCache[cacheKey] = token;
        return token.AccessToken;
    }

    /// <summary>
    /// Mints an access token via the password grant for an explicit
    /// <paramref name="clientId"/> and <paramref name="scope"/>. Unlike
    /// <see cref="GetAccessTokenAsync(string, string)"/> (which always uses
    /// <see cref="ClientId"/> + the legacy <c>sse.management</c> bundle), this
    /// lets a test exercise the webhook JWT path, where the token's
    /// <c>azp</c> must equal the integration's Keycloak clientId and the
    /// scope must contain the concrete <c>sse.events.write</c>.
    /// Not cached: each call requests fresh, since the client/scope pairing is
    /// per-test and short-lived.
    /// </summary>
    public async Task<string> GetAccessTokenForClientAsync(
        string clientId, string username, string password, string scope)
    {
        CachedToken token = await FetchAccessTokenAsync(username, password, clientId, scope)
            .ConfigureAwait(false);
        return token.AccessToken;
    }

    private Task<CachedToken> FetchAccessTokenAsync(string username, string password) =>
        FetchAccessTokenAsync(username, password, ClientId, "openid sse.management");

    private async Task<CachedToken> FetchAccessTokenAsync(
        string username, string password, string clientId, string scope)
    {
        using HttpClient keycloak = CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = scope,
        };

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token",
            new FormUrlEncodedContent(form)).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Keycloak password grant failed for '{username}': {response.StatusCode} {body}");
        }

        JsonElement tokenJson = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        string accessToken = tokenJson.GetProperty("access_token").GetString()!;
        int expiresIn = tokenJson.TryGetProperty("expires_in", out JsonElement expiresProperty)
            ? expiresProperty.GetInt32() : 60;

        return new CachedToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
