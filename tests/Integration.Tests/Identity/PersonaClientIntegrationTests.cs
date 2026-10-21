using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 428 (#2809) — persona tokens and password-grant probes mint through the confidential,
/// test-only <c>integration-test-persona</c> client, and <c>management-web</c> no longer accepts
/// a password grant.
///
/// <para>
/// <b>Red first (ADR-0139).</b> Written against a realm with no persona client (AS-1..AS-3 fail
/// with <c>invalid_client</c>) and a <c>management-web</c> that still answers 200 to a password
/// grant (AS-5). Raw POSTs, not the throwing fixture helpers, so each fact observes the status
/// and error code itself. Needs the Aspire stack on a fresh Keycloak volume.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class PersonaClientIntegrationTests(AspireFixture aspire)
{
    private const string PersonaClientId = "integration-test-persona";

    private const string PersonaClientSecret = "dev-only-integration-test-persona-secret";

    private const string ConsoleClientId = "management-web";

    private const string Operator = "op-dresden@dresden.test";

    private const string OperatorPassword = SeededCredentials.OpDresden;

    private const string OperatorFabGroup = "/fabs/dresden";

    private const string TokenPath = "/realms/smart-sentinel-eye/protocol/openid-connect/token";

    [Fact]
    public async Task A_persona_token_comes_from_the_test_client_and_names_the_users_fab()
    {
        var (status, body) = await PostAsync(PasswordForm(PersonaClientId, PersonaClientSecret));

        status.ShouldBe(HttpStatusCode.OK, $"persona password grant refused. Observed: {(int)status} {body}");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(body.GetProperty("access_token").GetString());

        token.Claims.Single(claim => claim.Type == "azp").Value.ShouldBe(PersonaClientId);
        token.Claims.Where(claim => claim.Type == "groups").Select(claim => claim.Value)
            .ShouldBe([OperatorFabGroup]);
    }

    [Fact]
    public async Task A_wrong_client_secret_is_refused_as_unauthorized_client()
    {
        var (status, body) = await PostAsync(PasswordForm(PersonaClientId, "wrong-secret"));

        status.ShouldBe(HttpStatusCode.Unauthorized, $"Observed: {(int)status} {body}");
        body.GetProperty("error").GetString().ShouldBe("unauthorized_client", $"Observed: {body}");
    }

    [Fact]
    public async Task The_test_client_is_not_a_service_account()
    {
        var (control, controlBody) = await PostAsync(PasswordForm(PersonaClientId, PersonaClientSecret));
        control.ShouldBe(HttpStatusCode.OK, $"control password grant failed. Observed: {(int)control} {controlBody}");

        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = PersonaClientId,
            ["client_secret"] = PersonaClientSecret,
        };
        var (status, body) = await PostAsync(form);

        status.ShouldBe(HttpStatusCode.Unauthorized, $"Observed: {(int)status} {body}");
        body.GetProperty("error").GetString().ShouldBe("unauthorized_client", $"Observed: {body}");
    }

    [Fact]
    public async Task The_console_client_refuses_a_password_grant()
    {
        var (control, controlBody) = await PostAsync(PasswordForm(PersonaClientId, PersonaClientSecret));
        control.ShouldBe(HttpStatusCode.OK, $"control password grant failed. Observed: {(int)control} {controlBody}");

        var (status, body) = await PostAsync(PasswordForm(ConsoleClientId, secret: null));

        status.ShouldBe(HttpStatusCode.BadRequest, $"Observed: {(int)status} {body}");
        body.GetProperty("error").GetString().ShouldBe("unauthorized_client", $"Observed: {body}");
    }

    private static Dictionary<string, string> PasswordForm(string clientId, string? secret)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = Operator,
            ["password"] = OperatorPassword,
            ["scope"] = "openid",
        };

        if (secret is not null)
        {
            form["client_secret"] = secret;
        }

        return form;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(Dictionary<string, string> form)
    {
        using var keycloak = aspire.CreateKeycloakClient();
        var response = await keycloak.PostAsync(TokenPath, new FormUrlEncodedContent(form));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body);
    }
}
