using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Caches the <c>identity-admin</c> client_credentials access
/// token across all Keycloak Admin API calls (spec 008 plan
/// §"Composition root + API"). Refreshes proactively at 80 % of
/// <c>expires_in</c> so a request never races a stale token.
/// </summary>
public sealed class KeycloakAdminTokenProvider(
    HttpClient httpClient,
    IOptions<KeycloakAdminOptions> options,
    TimeProvider clock,
    ILogger<KeycloakAdminTokenProvider> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private string? _cachedToken;
    private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

    public void Dispose() => _gate.Dispose();

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && clock.GetUtcNow() < _refreshAfter)
        {
            return _cachedToken;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null && clock.GetUtcNow() < _refreshAfter)
            {
                return _cachedToken;
            }

            KeycloakAdminOptions opts = options.Value;
            string url =
                $"{opts.BaseUrl.TrimEnd('/')}/realms/{opts.Realm}/protocol/openid-connect/token";

            FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.AdminClientId,
                ["client_secret"] = opts.AdminClientSecret,
            });

            HttpResponseMessage response = await httpClient
                .PostAsync(url, form, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            TokenResponse payload = await response.Content
                .ReadFromJsonAsync<TokenResponse>(JsonOpts, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Keycloak returned an empty token response.");

            _cachedToken = payload.AccessToken;
            // Refresh proactively at 80 % of the lifetime.
            _refreshAfter = clock.GetUtcNow().AddSeconds(payload.ExpiresIn * 0.8);

            Log.MintedAdminToken(logger, payload.ExpiresIn);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
