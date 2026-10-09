using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = SeededCredentials.Admin;

    /// <summary>
    /// The console client. Scope narrowed to persona tokens
    /// (<see cref="CreateAuthenticatedClientAsync"/>, non-admin
    /// <see cref="GetAccessTokenAsync(string, string, CancellationToken)"/>) and the
    /// password-grant probes that test <c>management-web</c>'s own ROPC behaviour. Admin
    /// tokens no longer mint here — see <see cref="HarnessClientId"/> and
    /// <see cref="GetAdminAccessTokenAsync"/> (spec 327, #2511).
    /// </summary>
    public const string ClientId = "management-web";

    /// <summary>
    /// The confidential <c>client_credentials</c> client the integration harness uses to mint
    /// admin tokens (spec 327, #2511), replacing the password grant this fixture previously
    /// made against <see cref="ClientId"/>.
    /// </summary>
    public const string HarnessClientId = "integration-test-admin";
    public const string HarnessClientSecret = "dev-only-integration-test-admin-secret";

    // Token cache lives across all tests in the collection so a 295-test
    // run does not hammer Keycloak with a fresh password grant per test
    // (same reasoning as Yumney's AspireFixture).
    private static readonly TimeSpan ExpirySafetyMargin = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, CachedToken> tokenCache = new();

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

    /// <summary>
    /// SignalR hub URI for a resource. Same reasoning as
    /// <see cref="CreateServiceClient"/>: <c>GetEndpoint</c> without an
    /// endpoint name inherits whatever default Aspire prefers, which 13.4.6
    /// changed to https. The hub tests build HubConnections rather than
    /// HttpClients, so they were a second route to the same bug (#1133).
    /// </summary>
    public Uri HubUri(string resourceName, string hubPath) =>
        new(App.GetEndpoint(resourceName, "http").ToString().TrimEnd('/') + hubPath);

    public async Task<HttpClient> CreateAdminClientAsync(
        string resourceName, CancellationToken cancellationToken = default)
    {
        string token = await GetAdminAccessTokenAsync(cancellationToken);
        HttpClient client = CreateServiceClient(resourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Mints the harness's admin token via <c>client_credentials</c> against
    /// <see cref="HarnessClientId"/>, sharing <see cref="tokenCache"/> with the password-grant
    /// path (spec 327, #2511).
    /// </summary>
    public async Task<string> GetAdminAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        string cacheKey = $"client_credentials|{HarnessClientId}";
        if (tokenCache.TryGetValue(cacheKey, out CachedToken? cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow + ExpirySafetyMargin)
        {
            return cached.AccessToken;
        }

        CachedToken token = await FetchClientCredentialsTokenAsync(cancellationToken);
        tokenCache[cacheKey] = token;
        return token.AccessToken;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(
        string resourceName, string username, string password, CancellationToken cancellationToken = default)
    {
        string token = await GetAccessTokenAsync(username, password, cancellationToken).ConfigureAwait(false);
        HttpClient client = CreateServiceClient(resourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> GetAccessTokenAsync(
        string username, string password, CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{username}|{password}";
        if (tokenCache.TryGetValue(cacheKey, out CachedToken? cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow + ExpirySafetyMargin)
        {
            return cached.AccessToken;
        }

        CachedToken token = await FetchAccessTokenAsync(username, password, cancellationToken)
            .ConfigureAwait(false);
        tokenCache[cacheKey] = token;
        return token.AccessToken;
    }

    /// <summary>
    /// Mints an access token via the password grant for an explicit
    /// <paramref name="clientId"/> and <paramref name="scope"/>. Unlike
    /// <see cref="GetAccessTokenAsync(string, string)"/> (which always uses
    /// <see cref="ClientId"/> + <c>openid</c>, granting whatever
    /// <c>management-web</c> default-grants), this lets a test exercise the
    /// webhook JWT path, where the token's <c>azp</c> must equal the
    /// integration's Keycloak clientId and the scope must contain the concrete
    /// <c>sse.events.write</c>.
    /// Not cached: each call requests fresh, since the client/scope pairing is
    /// per-test and short-lived.
    /// </summary>
    public async Task<string> GetAccessTokenForClientAsync(
        string clientId, string username, string password, string scope,
        CancellationToken cancellationToken = default)
    {
        CachedToken token = await FetchAccessTokenAsync(username, password, clientId, scope, cancellationToken)
            .ConfigureAwait(false);
        return token.AccessToken;
    }

    private Task<CachedToken> FetchAccessTokenAsync(
        string username, string password, CancellationToken cancellationToken) =>
        FetchAccessTokenAsync(username, password, ClientId, "openid", cancellationToken);

    private Task<CachedToken> FetchAccessTokenAsync(
        string username, string password, string clientId, string scope,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = scope,
        };

        return RequestTokenAsync(form, $"password grant failed for '{username}'", cancellationToken);
    }

    private Task<CachedToken> FetchClientCredentialsTokenAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = HarnessClientId,
            ["client_secret"] = HarnessClientSecret,
        };

        return RequestTokenAsync(
            form, $"client_credentials grant failed for '{HarnessClientId}'", cancellationToken);
    }

    /// <summary>
    /// Shared POST + status check + JSON parse for every grant this fixture mints.
    /// <paramref name="describe"/> names the grant and the principal, so a failure reads
    /// e.g. "Keycloak password grant failed for 'admin'" or "Keycloak client_credentials
    /// grant failed for 'integration-test-admin'" rather than one generic message (spec 327).
    /// </summary>
    private async Task<CachedToken> RequestTokenAsync(
        Dictionary<string, string> form, string describe, CancellationToken cancellationToken)
    {
        using HttpClient keycloak = CreateKeycloakClient();
        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token",
            new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Keycloak {describe}: {response.StatusCode} {body}");
        }

        JsonElement tokenJson = await response.Content
            .ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        string accessToken = tokenJson.GetProperty("access_token").GetString()!;
        int expiresIn = tokenJson.TryGetProperty("expires_in", out JsonElement expiresProperty)
            ? expiresProperty.GetInt32() : 60;

        return new CachedToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt);
}
