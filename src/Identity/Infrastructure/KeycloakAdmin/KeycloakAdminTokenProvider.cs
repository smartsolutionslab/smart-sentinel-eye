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
    private readonly SemaphoreSlim gate = new(initialCount: 1, maxCount: 1);
    private string? cachedToken;
    private DateTimeOffset refreshAfter = DateTimeOffset.MinValue;

    public void Dispose() => gate.Dispose();

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (cachedToken is not null && clock.GetUtcNow() < refreshAfter)
        {
            return cachedToken;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cachedToken is not null && clock.GetUtcNow() < refreshAfter)
            {
                return cachedToken;
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
                .PostAsync(url, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            TokenResponse payload = await response.Content
                .ReadFromJsonAsync<TokenResponse>(JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Keycloak returned an empty token response.");

            cachedToken = payload.AccessToken;
            // Refresh proactively at 80 % of the lifetime.
            refreshAfter = clock.GetUtcNow().AddSeconds(payload.ExpiresIn * 0.8);

            logger.MintedAdminToken(payload.ExpiresIn);
            return cachedToken;
        }
        finally
        {
            gate.Release();
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
