using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 423 (#1467), R5. A stream's fab is its camera's, written at
/// provisioning, so the cross-fab service account StreamDistribution used to
/// re-read the camera catalog with (ADR-0116) has no remaining purpose and is
/// removed from the realm. The credentials below are the ones the realm import
/// used to seed; a token for them must now be refused.
///
/// <para>
/// <b>Red on arrival, for the right reason.</b> Until the realm import drops
/// the client, Keycloak answers 200 and issues a token.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RetiredAttributionClientIntegrationTests(AspireFixture aspire)
{
    private const string RetiredClientIdentifier = "stream-distribution-attribution";
    private const string RetiredClientSecret = "dev-only-stream-distribution-secret";

    [Fact]
    public async Task The_retired_attribution_client_is_refused()
    {
        using var keycloak = aspire.CreateKeycloakClient();
        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = RetiredClientIdentifier,
            ["client_secret"] = RetiredClientSecret,
        });

        var response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, body);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;
        problem.GetProperty("error").GetString().ShouldBe("invalid_client");
    }
}
