using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

/// <summary>
/// Mints the <c>stream-distribution-attribution</c> client_credentials token
/// the startup attribution pass presents to CameraCatalog (ADR-0116). Mirrors
/// EventIngestion's <c>MqttTokenProvider</c>.
///
/// <para>
/// Deliberately not cached, unlike its siblings: attribution runs once per
/// host start and asks for one token. A cache with a refresh window would be
/// machinery for a second call that never happens.
/// </para>
/// </summary>
public sealed class CameraCatalogTokenProvider(
    HttpClient httpClient,
    IOptions<StreamFabAttributionOptions> options)
{
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        StreamFabAttributionOptions settings = options.Value;
        string url = $"{settings.KeycloakUrl.TrimEnd('/')}/realms/{settings.Realm}/protocol/openid-connect/token";

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = settings.ClientIdentifier,
            ["client_secret"] = settings.ClientSecret,
        });

        using HttpResponseMessage response = await httpClient.PostAsync(url, form, cancellationToken);
        response.EnsureSuccessStatusCode();

        TokenResponse payload = await response.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Keycloak returned an empty token response.");

        return payload.AccessToken;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);
}
