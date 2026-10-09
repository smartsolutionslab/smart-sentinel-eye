using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 327 (#2511) — the integration-test harness's admin path stops minting
/// its token by <c>grant_type=password</c> against <c>management-web</c> and
/// mints it instead through a dedicated confidential
/// <c>integration-test-admin</c> service-account client via
/// <c>client_credentials</c>, mirroring <c>identity-admin</c>'s shape
/// (ADR-0100).
///
/// <para>
/// <b>Red today, deliberately.</b> This class compiles against the fixture
/// as it exists now — <c>CreateAdminClientAsync</c> still mints by
/// <c>grant_type=password</c> against <c>management-web</c>, and no
/// <c>integration-test-admin</c> client exists in the realm. <see
/// cref="The_admin_harness_token_is_minted_by_the_integration_test_admin_client"/>
/// is the slice's defining red (today's <c>azp</c> is <c>management-web</c>).
/// <see
/// cref="The_admin_harness_token_carries_the_admin_users_fab_and_the_consoles_scopes"/>
/// is the declared characterisation half — green today, and it must pass
/// <b>unmodified</b> after the migration, because it is the proof the
/// migration does not drop a claim the 393 admin call sites rely on.
/// </para>
///
/// <para>
/// <b>Control-first in facts 3 and 4.</b> The client does not exist yet, so a
/// wrong-secret or a password-grant probe against it today gets exactly the
/// same <c>invalid_client</c>/401 a genuinely-missing client would give —
/// which would make either fact pass for the wrong reason. Each fact first
/// asserts that the <i>correct</i> <c>client_credentials</c> call succeeds,
/// which fails today because the client is absent, so the whole fact is red
/// for the right reason rather than green by accident.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AdminHarnessTokenIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// Local on purpose, exactly as <c>RealmProbe</c> keeps its own copy of
    /// <c>identity-admin</c>'s id/secret: the fixture's <c>HarnessClientId</c>
    /// / <c>HarnessClientSecret</c> constants do not exist yet (plan.md §5,
    /// T-B — "use only existing fixture members").
    /// </summary>
    private const string HarnessClientId = "integration-test-admin";

    private const string HarnessClientSecret = "dev-only-integration-test-admin-secret";

    private const string ConsoleClientId = "management-web";

    private const string AdminFabGroup = "/fabs/munich";

    private const string ApiAudience = "smart-sentinel-eye-api";

    /// <summary>Any resource works — this class only ever reads the bearer header back off it.</summary>
    private const string ProbeResource = "camera-catalog";

    [Fact]
    public async Task The_admin_harness_token_is_minted_by_the_integration_test_admin_client()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync(ProbeResource);
        string token = TokenOf(client);

        AzpOf(token).ShouldBe(
            HarnessClientId,
            customMessage: $"the admin harness token's azp is '{AzpOf(token)}', not "
            + $"'{HarnessClientId}'. CreateAdminClientAsync still mints via grant_type=password "
            + $"against '{AspireFixture.ClientId}' (spec 327 §Decision — this is the slice's "
            + "defining red).");
    }

    /// <summary>
    /// The characterisation half (plan.md §5, T-B fact 2). Green today against
    /// the <c>management-web</c> token <c>CreateAdminClientAsync</c> currently
    /// mints, and it must stay green, unmodified, once the harness client
    /// replaces it — proving the migration does not quietly drop a claim one
    /// of the 393 migrated admin sites depends on.
    /// </summary>
    [Fact]
    public async Task The_admin_harness_token_carries_the_admin_users_fab_and_the_consoles_scopes()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync(ProbeResource);
        string token = TokenOf(client);

        GroupsOf(token).ShouldContain(
            AdminFabGroup,
            customMessage: "the admin harness token must carry the admin user's fab group, or "
            + "every fab-scoped admin call the migrated sites make is refused (spec 327 AS-1).");

        IReadOnlyCollection<string> granted = ScopesOf(token);
        IReadOnlyCollection<string> expected = ExpectedGrantedScopes();

        expected.Except(granted, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"the admin harness token is missing a scope '{ConsoleClientId}' "
            + $"default-grants. Expected at least: {string.Join(" ", expected)}; granted: "
            + $"{string.Join(" ", granted)}. Losing one would make an admin site that works today "
            + "start failing after the migration (spec 327 AS-1).");

        AudiencesOf(token).ShouldContain(
            ApiAudience,
            customMessage: $"the admin harness token must name '{ApiAudience}' (spec 069 FR-003).");

        Guid.TryParse(SubjectOf(token), out Guid subject).ShouldBeTrue(
            "the admin harness token's sub must parse as a Guid, exactly like every other minted "
            + "token's (spec 042).");
        subject.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task A_wrong_secret_for_the_integration_test_admin_client_is_refused()
    {
        // Control first: the correct secret must mint a token, or the refusal
        // below proves nothing about the secret specifically. Today the client
        // does not exist, so this control itself fails with invalid_client/401
        // — the same answer a wrong secret would get against a real client, so
        // this is red for the right reason rather than a fact that would have
        // passed by accident.
        (HttpStatusCode controlStatus, JsonElement controlBody) = await ClientCredentialsGrantAsync(
            HarnessClientId, HarnessClientSecret);

        controlStatus.ShouldBe(
            HttpStatusCode.OK,
            customMessage: "control: client_credentials with the correct secret must succeed, or "
            + $"the wrong-secret refusal below proves nothing. Observed: {(int)controlStatus} {controlBody}");

        (HttpStatusCode status, JsonElement body) = await ClientCredentialsGrantAsync(
            HarnessClientId, "wrong-secret");

        status.ShouldBe(
            HttpStatusCode.Unauthorized,
            customMessage: $"a wrong secret for '{HarnessClientId}' must be refused with 401 "
            + $"(AS-4). Observed: {(int)status} {body}");
    }

    [Fact]
    public async Task The_integration_test_admin_client_refuses_a_password_grant()
    {
        // Control first, same reasoning as the fact above: client_credentials
        // with the correct secret must succeed before the password-grant
        // refusal means anything about direct access grants specifically.
        (HttpStatusCode controlStatus, JsonElement controlBody) = await ClientCredentialsGrantAsync(
            HarnessClientId, HarnessClientSecret);

        controlStatus.ShouldBe(
            HttpStatusCode.OK,
            customMessage: "control: client_credentials with the correct secret must succeed, or "
            + $"the password-grant refusal below proves nothing. Observed: {(int)controlStatus} {controlBody}");

        (HttpStatusCode status, JsonElement body) = await PasswordGrantAsync(
            HarnessClientId, AspireFixture.AdminUsername, AspireFixture.AdminPassword, HarnessClientSecret);

        status.ShouldBe(
            HttpStatusCode.BadRequest,
            customMessage: $"'{HarnessClientId}' has directAccessGrantsEnabled=false and must "
            + $"refuse a password grant (AS-4). Observed: {(int)status} {body}");
        body.TryGetProperty("error", out JsonElement error).ShouldBeTrue(
            $"expected an 'error' field on the refusal. Observed: {(int)status} {body}");
        error.GetString().ShouldBe(
            "unauthorized_client",
            customMessage: $"the refusal must be about the grant type, not the credentials. "
            + $"Observed: {(int)status} {body}");
    }

    /// <summary>
    /// T-D (plan.md §5): admin is one identity (spec 327 AS-3). Many tests mix
    /// <c>CreateAdminClientAsync</c> with a raw admin token in one flow —
    /// <c>StaleIdempotencyReservationIntegrationTests</c> seeds a reservation
    /// under one and replays under the other — so both accessors must present
    /// the same <c>sub</c>. Counterfactual-observed red: point
    /// <c>GetAdminAccessTokenAsync</c> at the password grant instead of
    /// <c>client_credentials</c>, watch this fail on a mismatched subject,
    /// then revert.
    /// </summary>
    [Fact]
    public async Task Every_admin_accessor_presents_the_same_subject()
    {
        using HttpClient client = await aspire.CreateAdminClientAsync(ProbeResource);
        string? clientSubject = SubjectOf(TokenOf(client));
        clientSubject.ShouldNotBeNullOrEmpty("CreateAdminClientAsync's token carries no sub claim.");

        string rawToken = await aspire.GetAdminAccessTokenAsync();
        string? rawSubject = SubjectOf(rawToken);
        rawSubject.ShouldNotBeNullOrEmpty("GetAdminAccessTokenAsync()'s token carries no sub claim.");

        rawSubject.ShouldBe(
            clientSubject,
            customMessage: $"CreateAdminClientAsync's token has sub '{clientSubject}' but "
            + $"GetAdminAccessTokenAsync()'s token has sub '{rawSubject}' — every admin accessor "
            + "must present the same identity, or a reservation seeded under one and replayed "
            + "under the other silently fails (spec 327 AS-3).");
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> ClientCredentialsGrantAsync(
        string clientId, string clientSecret)
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

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body);
    }

    /// <summary>
    /// <paramref name="clientSecret"/> is optional because <see
    /// cref="ConsoleClientId"/> is a public client and has no secret to send.
    /// A confidential client such as <see cref="HarnessClientId"/> must pass
    /// one, or Keycloak rejects the call for failed client authentication
    /// (401 invalid_client) before it ever reaches the direct-access-grants
    /// check this probe exists to observe.
    /// </summary>
    private async Task<(HttpStatusCode Status, JsonElement Body)> PasswordGrantAsync(
        string clientId, string username, string password, string? clientSecret = null)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        Dictionary<string, string> fields = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        };

        if (clientSecret is not null)
        {
            fields["client_secret"] = clientSecret;
        }

        using FormUrlEncodedContent form = new(fields);

        HttpResponseMessage response = await keycloak.PostAsync(
            "/realms/smart-sentinel-eye/protocol/openid-connect/token", form);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body);
    }

    /// <summary>
    /// Every scope '<see cref="ConsoleClientId"/>' default-grants whose
    /// client scope actually lands in the token (<c>include.in.token.scope ==
    /// "true"</c>) — read fresh from the realm file rather than a literal
    /// list, the same path helper <c>RealmImportMirrorTests</c> uses, so this
    /// fact tracks the realm rather than a snapshot of it.
    /// </summary>
    private static IReadOnlyCollection<string> ExpectedGrantedScopes()
    {
        using JsonDocument realm = ReadRealmImport();

        JsonElement console = realm.RootElement.GetProperty("clients").EnumerateArray()
            .First(client => client.GetProperty("clientId").GetString() == ConsoleClientId);
        IReadOnlyCollection<string> consoleScopeNames = [.. console
            .GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)];

        Dictionary<string, JsonElement> scopesByName = realm.RootElement
            .GetProperty("clientScopes").EnumerateArray()
            .ToDictionary(scope => scope.GetProperty("name").GetString() ?? string.Empty, scope => scope);

        return
        [
            .. consoleScopeNames.Where(name =>
                scopesByName.TryGetValue(name, out JsonElement scope)
                && scope.TryGetProperty("attributes", out JsonElement attributes)
                && attributes.TryGetProperty("include.in.token.scope", out JsonElement include)
                && include.GetString() == "true"),
        ];
    }

    private static JsonDocument ReadRealmImport()
    {
        string path = Path.Combine(
            RepositoryRoot(), "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");

        File.Exists(path).ShouldBeTrue($"the realm import should be at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }

    private static string TokenOf(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization?.Parameter
        ?? throw new InvalidOperationException(
            "the client carries no bearer token to read back — CreateAdminClientAsync should "
            + "always attach one.");

    private static string? AzpOf(string token) => ClaimOf(token, "azp");

    private static string? SubjectOf(string token) => ClaimOf(token, "sub");

    private static string? ClaimOf(string token, string claimType)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return handler.ReadJwtToken(token).Claims
            .FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            ?.Value;
    }

    private static IReadOnlyList<string> GroupsOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return
        [
            .. handler.ReadJwtToken(token).Claims
                .Where(claim => string.Equals(claim.Type, "groups", StringComparison.Ordinal))
                .Select(claim => claim.Value),
        ];
    }

    private static IReadOnlyCollection<string> ScopesOf(string token)
    {
        string? scope = ClaimOf(token, "scope");

        return scope is null
            ? []
            : [.. scope.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static IReadOnlyCollection<string> AudiencesOf(string token)
    {
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return [.. handler.ReadJwtToken(token).Audiences];
    }
}
