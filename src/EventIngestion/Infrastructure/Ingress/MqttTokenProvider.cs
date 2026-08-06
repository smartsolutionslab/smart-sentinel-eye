using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Mints + caches the <c>event-ingestion</c> client_credentials access token
/// the MQTT subscriber presents as its broker password (ADR-0100). Mirrors
/// the Scenario Simulator's <c>KeycloakTokenProvider</c> and the Identity
/// API's <c>KeycloakAdminTokenProvider</c>: refresh proactively at 80 % of
/// <c>expires_in</c> so a reconnect never presents an expired JWT.
/// </summary>
public sealed class MqttTokenProvider(
    HttpClient httpClient,
    IOptions<MosquittoOptions> options,
    IClock clock,
    ILogger<MqttTokenProvider> logger) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);
    private string? _cachedToken;
    private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

    public void Dispose() => _gate.Dispose();

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && clock.UtcNow < _refreshAfter)
        {
            return _cachedToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && clock.UtcNow < _refreshAfter)
            {
                return _cachedToken;
            }

            MosquittoOptions opts = options.Value;
            string url = $"{opts.KeycloakUrl.TrimEnd('/')}/realms/{opts.Realm}/protocol/openid-connect/token";

            FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.Username,
                ["client_secret"] = opts.ClientSecret,
            });

            using HttpResponseMessage response = await httpClient.PostAsync(url, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            TokenResponse payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

            _cachedToken = payload.AccessToken;
            _refreshAfter = clock.UtcNow.AddSeconds(payload.ExpiresIn * 0.8);

            logger.MintedMqttToken(payload.ExpiresIn);
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
