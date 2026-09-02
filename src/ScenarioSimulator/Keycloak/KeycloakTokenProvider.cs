using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;

namespace SmartSentinelEye.ScenarioSimulator.Keycloak;

/// <summary>
/// Mints + caches the <c>scenario-simulator</c> client_credentials access token
/// the worker uses to call camera-catalog. Mirrors the Identity API's
/// <c>KeycloakAdminTokenProvider</c> (spec 008): refresh proactively at 80 % of
/// <c>expires_in</c> so a request never races a stale token. Dev-only client.
/// </summary>
public sealed class KeycloakTokenProvider(
    HttpClient httpClient,
    IOptions<SimulatorOptions> options,
    TimeProvider clock,
    ILogger<KeycloakTokenProvider> logger) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

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

            SimulatorOptions opts = options.Value;
            string url = $"{opts.KeycloakUrl.TrimEnd('/')}/realms/{opts.Realm}/protocol/openid-connect/token";

            FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = opts.ClientId,
                ["client_secret"] = opts.ClientSecret,
            });

            using HttpResponseMessage response = await httpClient.PostAsync(url, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            TokenResponse? payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

            cachedToken = payload.AccessToken;
            refreshAfter = clock.GetUtcNow().AddSeconds(payload.ExpiresIn * 0.8);

            logger.MintedSimulatorToken(payload.ExpiresIn);
            return cachedToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string TokenType);
}
