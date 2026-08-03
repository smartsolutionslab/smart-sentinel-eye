using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 013 — every <c>/rules</c> endpoint now refuses a caller who holds no
/// fab, so the realm has to actually hand out the group. Two ways it silently
/// does not: the user is in no fab group, or their client omits
/// <c>sse-groups</c> — the only scope carrying the group-membership mapper, so
/// without it a correctly-grouped principal still arrives with no
/// <c>groups</c> claim.
///
/// <para>
/// Both failures look identical from the outside (403) and neither is visible
/// in the realm file without cross-referencing two sections, which is how the
/// scenario-simulator was given the group and still could not seed. The
/// principals asserted here are the two that no other test covers: the
/// simulator's service account (dev-only, gated out of e2e) and the seeded
/// <c>operator</c> the Playwright sign-in uses. The <c>admin</c> account is
/// covered by <c>CrossFabEvaluationIntegrationTests</c>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class FabGroupClaimIntegrationTests(AspireFixture aspire)
{
    private const string FabGroup = "/fabs/munich";
    private const string GroupsClaim = "groups";

    [Fact]
    public async Task The_seeded_operator_arrives_with_its_fab_group()
    {
        GroupsOf(await OperatorTokenAsync()).ShouldContain(FabGroup);
    }

    [Fact]
    public async Task The_seeded_operator_can_author_a_rule()
    {
        // What the group is for. Before this, every /rules endpoint answered
        // the operator 403 — including the ones e2e/rules.spec.ts drives.
        HttpResponseMessage created = await AuthorRuleAsync(await OperatorTokenAsync(), null, UniqueName());

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));
    }

    [Fact]
    public async Task The_scenario_simulator_service_account_arrives_with_its_fab_group()
    {
        string token = await ServiceAccountTokenAsync();

        GroupsOf(token).ShouldContain(FabGroup);
    }

    [Fact]
    public async Task The_scenario_simulator_service_account_can_seed_a_rule()
    {
        // The seeder names its fab explicitly, so this covers the guard path
        // the simulator actually takes on startup.
        string token = await ServiceAccountTokenAsync();

        HttpResponseMessage created = await AuthorRuleAsync(token, "munich", UniqueName());

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));
    }

    /// <summary>
    /// The seeder's repair path rests on this: on 409 it reads the rule back to
    /// learn whether an earlier run left it in Draft. <c>GET /rules</c> — the
    /// listing — returns 500 (#1298), and it would be easy to assume the
    /// by-name read is equally broken and quietly give up on the repair. It is
    /// not, and nothing else asserts a successful read of a rule over HTTP.
    /// </summary>
    [Fact]
    public async Task A_rule_can_be_read_back_by_name_with_its_state_and_version()
    {
        string token = await ServiceAccountTokenAsync();
        string name = UniqueName();

        HttpResponseMessage created = await AuthorRuleAsync(token, "munich", name);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await DiagnoseAsync(created));

        using HttpClient rules = aspire.CreateServiceClient("automation");
        rules.DefaultRequestHeaders.Authorization = new("Bearer", token);

        HttpResponseMessage read = await rules.GetAsync($"/rules/{name}?fabId=munich");
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await DiagnoseAsync(read));

        JsonElement rule = await read.Content.ReadFromJsonAsync<JsonElement>();
        rule.GetProperty("state").GetString().ShouldBe("Draft");
        rule.GetProperty("version").GetInt32().ShouldBe(0);
    }

    private async Task<HttpResponseMessage> AuthorRuleAsync(string token, string? fabId, string name)
    {
        using HttpClient rules = aspire.CreateServiceClient("automation");
        rules.DefaultRequestHeaders.Authorization = new("Bearer", token);

        string path = fabId is null ? "/rules" : $"/rules?fabId={fabId}";

        return await rules.PostAsJsonAsync(path, new
        {
            name,
            triggerSource = "plc",
            triggerKind = "PlcCycleStart",
            predicate = "$.payload.cycleTime <= 30",
            actionType = "SetVariableValue",
            variableName = "oeeLine1",
            valueExpression = "100 - $.payload.cycleTime * 2",
            overlayIdentifier = (Guid?)null,
            durationMs = (int?)null,
        });
    }

    private static string UniqueName() => $"r-{Guid.NewGuid():N}"[..12];

    /// <summary>
    /// Signs in as the seeded operator through <c>smart-sentinel-eye-web</c> —
    /// the client <c>apps/management-web/src/app/auth.ts</c> actually
    /// configures, not the same-named <c>management-web</c> realm client, which
    /// nothing uses yet and which carries no <c>sub</c> mapper.
    /// </summary>
    private Task<string> OperatorTokenAsync() =>
        aspire.GetAccessTokenForClientAsync(
            AspireFixture.ClientId, "operator", "Operator1234", "openid sse.management");

    /// <summary>
    /// The client_credentials grant the simulator worker uses. Not on the
    /// fixture: it is the only service account any test needs a token for, and
    /// the secret is the dev-only one from the realm.
    /// </summary>
    private async Task<string> ServiceAccountTokenAsync()
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "scenario-simulator",
            ["client_secret"] = "dev-only-scenario-simulator-secret",
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("access_token").GetString()!;
    }

    private static IReadOnlyList<string> GroupsOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return
        [
            .. handler.ReadJwtToken(token).Claims
                .Where(claim => string.Equals(claim.Type, GroupsClaim, StringComparison.Ordinal))
                .Select(claim => claim.Value),
        ];
    }

    private async Task<string> DiagnoseAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}automation log:{Environment.NewLine}{aspire.RecentLogs("automation")}";
    }
}
