using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 073, issue 2070 — the refusal, on the real stack. <c>sse.variables.read</c>
/// is in the catalogue and in the realm, granted by name to <c>kiosk-web</c>,
/// <c>kiosk-wall</c> and <c>management-web</c>; until spec 073 the three
/// <c>/system-variables</c> reads required only authentication, so a principal
/// holding a fab and none of that scope read every variable in it.
///
/// <para>
/// <b>The principal is the point.</b> <see cref="AspireFixture.ClientId"/> is
/// <c>smart-sentinel-eye-web</c> and the fixture's own token always asks for
/// <c>openid sse.management</c> — a bundle that satisfies every <c>sse.*</c>
/// policy but <c>sse.events.publish</c>. A test built on
/// <see cref="AspireFixture.CreateAdminClientAsync"/> therefore answers 200
/// before this change and 200 after, which is precisely how the gap survived
/// long enough to be found by a review of an unrelated PR. So this mints a
/// <c>client_credentials</c> token for <c>scenario-simulator</c>: a real service
/// account in <c>/fabs/munich</c> whose eleven default scopes cover cameras,
/// overlays, rules and layouts and do <b>not</b> include
/// <c>sse.variables.read</c>.
/// </para>
///
/// <para>
/// <b>403 exactly, never merely "not 200".</b> Spec 069's audience check also
/// produces a non-200, and so does a mis-minted token (401); pinning the status
/// keeps a broken fixture from reading as a successful refusal. The admin client
/// is exercised in the same test for the same reason, from the other side — a
/// stack that refused everybody would pass a refusal assertion on its own.
/// </para>
///
/// <para>
/// Minted from the fixture's Keycloak client, which points at Aspire's proxied
/// endpoint. A token minted from the container's mapped port carries an issuer
/// the API does not accept, and every call 401s regardless of scope.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class VariableReadScopeIntegrationTests(AspireFixture aspire)
{
    private const string ServiceAccountClientId = "scenario-simulator";
    private const string ServiceAccountSecret = "dev-only-scenario-simulator-secret";
    private const string Listing = "/system-variables";

    public static TheoryData<string> Reads() =>
    [
        Listing,
        // Authorization runs ahead of model binding, so the identifier and the
        // name below need not exist. Before the scope lands these answer 404;
        // after it, 403 — the caller never reaches the lookup.
        $"/system-variables/snapshot?overlayIdentifier={Guid.Empty}",
        "/system-variables/a-name-no-fab-holds",
    ];

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_caller_without_the_variables_read_scope_is_refused(string route)
    {
        using HttpClient admin = await aspire.CreateAdminClientAsync("system-variables");
        HttpResponseMessage control = await admin.GetAsync(Listing);
        control.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the positive control failed: a caller who does hold sse.variables.read cannot read the "
            + $"listing either, so the refusal below would prove nothing. {await BodyAsync(control)}");

        using HttpClient unscoped = await ServiceAccountClientAsync();

        HttpResponseMessage refused = await unscoped.GetAsync(route);

        refused.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"GET {route} admitted a principal holding no sse.variables.read. The service account is in "
            + "/fabs/munich and carries sse-audience, so neither the fab guard nor spec 069's audience "
            + $"check explains this answer — the scope is enforced by nothing. {await BodyAsync(refused)}");
    }

    /// <summary>
    /// Names the scopes the refused principal does hold, so a future failure
    /// reads as "the realm changed" rather than "the endpoint regressed". If
    /// <c>scenario-simulator</c> is ever granted <c>sse.variables.read</c>, this
    /// fails first and the theory above becomes meaningless — swap in
    /// <c>stream-distribution-attribution</c>, likewise in Munich and likewise
    /// without the scope.
    /// </summary>
    [Fact]
    public async Task The_refused_service_account_holds_a_fab_but_not_the_read_scope()
    {
        string token = await ServiceAccountTokenAsync();

        Claims(token, "scope").ShouldNotContain(scope => scope.Contains("sse.variables.read", StringComparison.Ordinal));
        Claims(token, "groups").ShouldContain("/fabs/munich");
    }

    /// <summary>
    /// The <c>client_credentials</c> grant, hand-rolled here rather than added
    /// to <see cref="AspireFixture"/>: <c>FabGroupClaimIntegrationTests</c> and
    /// <c>StreamFabAttributionIntegrationTests</c> each hold their own, and two
    /// call sites are not yet a pattern (ADR-0036).
    /// </summary>
    private async Task<string> ServiceAccountTokenAsync()
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();

        using FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ServiceAccountClientId,
            ["client_secret"] = ServiceAccountSecret,
        });

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(response));

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        return payload.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpClient> ServiceAccountClientAsync()
    {
        HttpClient client = aspire.CreateServiceClient("system-variables");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await ServiceAccountTokenAsync());
        return client;
    }

    private static IReadOnlyList<string> Claims(string token, string claim)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return
        [
            .. handler.ReadJwtToken(token).Claims
                .Where(each => string.Equals(each.Type, claim, StringComparison.Ordinal))
                .Select(each => each.Value),
        ];
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}";
}
