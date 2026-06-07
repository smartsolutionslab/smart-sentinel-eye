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

    public Task<HttpClient> CreateAdminClientAsync(string resourceName) =>
        CreateAuthenticatedClientAsync(resourceName, AdminUsername, AdminPassword);

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string resourceName, string username, string password)
    {
        string token = await GetAccessTokenAsync(username, password).ConfigureAwait(false);
        HttpClient client = App.CreateHttpClient(resourceName);
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
        using HttpClient keycloak = App.CreateHttpClient("keycloak");
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
