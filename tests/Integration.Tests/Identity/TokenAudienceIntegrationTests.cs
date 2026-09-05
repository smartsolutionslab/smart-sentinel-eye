using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 069 — a real minted token names the API it is for.
///
/// <para>
/// <b>Why this exists when RealmAudienceTests already reads the file.</b>
/// Reading names cannot see a mapper that is present and does not fire. A
/// mistyped Keycloak config key is discarded at import with a warning nobody
/// reads — this realm has already lost thirty-two scope names exactly that way
/// (spec 042) — and the file would still say the audience is configured. This is
/// the only assertion that decodes what Keycloak actually issued.
/// </para>
///
/// <para>
/// <b>Three populations, because the audience has to reach all of them.</b> A
/// person's token through the browser client, a service account's token through
/// <c>client_credentials</c>, and a client this system created at runtime — which
/// is in no realm file at all, so nothing else here would notice it losing the
/// claim. <c>WebhookBearerValidationIntegrationTests</c> substitutes
/// <c>management-web</c> for a rotated client, so the suite stays green while
/// every runtime-created client is refused.
/// </para>
///
/// <para>
/// <b>No negative here, deliberately.</b> Minting an audience-less token needs a
/// client that contradicts FR-003, and a test that rewrites the realm under a
/// running stack is worse than the documented drill. The negative lives in
/// <c>BearerAudienceTests</c> as the exact validation function, and in the
/// phase-5 procedure as a real 401.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class TokenAudienceIntegrationTests(AspireFixture aspire)
{
    private const string ApiAudience = "smart-sentinel-eye-api";

    private const string SimulatorClientId = "scenario-simulator";
    private const string SimulatorClientSecret = "dev-only-scenario-simulator-secret";

    [Fact]
    public async Task A_minted_token_names_the_api_it_is_for()
    {
        string token = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        AudiencesOf(token).ShouldContain(ApiAudience,
            customMessage: $"the token '{AspireFixture.ClientId}' mints does not name "
            + $"'{ApiAudience}', so every API refuses it once audience validation is on. This is "
            + "the client every other integration test authenticates through (spec 069 FR-003).");
    }

    [Fact]
    public async Task A_service_accounts_token_names_it_too()
    {
        string token = await MintClientCredentialsTokenAsync(SimulatorClientId, SimulatorClientSecret);

        AudiencesOf(token).ShouldContain(ApiAudience,
            customMessage: $"the '{SimulatorClientId}' service account mints a token that does not "
            + $"name '{ApiAudience}'. The machine half of the inventory goes through "
            + "client_credentials, which requests no scope by name — so the audience has to be a "
            + "default client scope, not an optional one (spec 069 FR-006).");
    }

    /// <summary>
    /// <b>The one no file can guard.</b> Device registration creates a Keycloak
    /// client through the Admin API, so it is in no realm file — its scopes come
    /// from <c>KeycloakScopeBundles</c>, and its token is what an inference box
    /// on the plant floor presents.
    /// </summary>
    [Fact]
    public async Task A_client_enrolled_at_runtime_mints_a_token_that_names_it()
    {
        string adminToken = await aspire.GetAccessTokenAsync(
            AspireFixture.AdminUsername, AspireFixture.AdminPassword);

        (string clientId, string clientSecret) = await RegisterDeviceAsync(adminToken);
        string token = await MintClientCredentialsTokenAsync(clientId, clientSecret);

        AudiencesOf(token).ShouldContain(ApiAudience,
            customMessage: $"'{clientId}' was created at runtime and mints a token that does not "
            + $"name '{ApiAudience}'. No realm guard can see this client, because it is in no realm "
            + "file (spec 069 FR-005).");
    }

    private async Task<(string ClientId, string ClientSecret)> RegisterDeviceAsync(string adminToken)
    {
        using HttpClient identity = aspire.CreateServiceClient("identity");
        using HttpRequestMessage request = new(HttpMethod.Post, "/devices/register?fabId=munich")
        {
            Content = JsonContent.Create(new
            {
                deviceType = "plc",
                deviceIdentifier = $"audience-{Guid.CreateVersion7():N}",
            }),
        };
        request.Headers.Authorization = new("Bearer", adminToken);

        HttpResponseMessage response = await identity.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /devices/register failed with {(int)response.StatusCode} {response.StatusCode}. "
                + $"Body: {body}{Environment.NewLine}identity log:{Environment.NewLine}"
                + aspire.RecentLogs("identity"));
        }

        JsonElement registered = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (registered.GetProperty("clientId").GetString()!,
            registered.GetProperty("clientSecret").GetString()!);
    }

    private async Task<string> MintClientCredentialsTokenAsync(string clientId, string clientSecret)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// The <c>aud</c> claim as Keycloak issued it. Read rather than validated —
    /// this asserts what is in the token, not whether a handler would accept it.
    /// </summary>
    private static IReadOnlyCollection<string> AudiencesOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return [.. handler.ReadJwtToken(token).Audiences];
    }
}
