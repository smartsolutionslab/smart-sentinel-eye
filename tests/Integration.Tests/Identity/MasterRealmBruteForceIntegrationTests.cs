using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 324 (issue #2508) — the Keycloak <c>master</c> realm, home of the
/// bootstrap <c>admin</c>/<c>admin-cli</c> account, has no brute-force
/// protection today (<c>bruteForceProtected: false</c>), unlike
/// <c>smart-sentinel-eye</c> (spec 207, <see cref="BruteForceLockoutIntegrationTests"/>).
/// A token from <c>master</c>'s admin can rewrite every realm, including
/// turning spec 207's protection back off, so it is the highest-value
/// credential in the deployment and currently the least protected.
///
/// <para>
/// <b>Colour (tasks.md phase 4a):</b>
/// <see cref="The_master_realm_is_brute_force_protected_with_the_application_realms_settings"/>
/// and
/// <see cref="A_master_realm_account_is_refused_its_correct_password_after_too_many_wrong_ones"/>
/// are <b>red</b> — `master` accepts unlimited password guesses until
/// <c>src/AppHost/Realms/master-realm.json</c> exists (phase 4b).
/// <see cref="The_bootstrap_admin_still_authenticates_and_master_keeps_its_bootstrap_token_lifespan"/>
/// is a <b>guard, green before and after, unmodified</b> — plan.md §5 test 3:
/// it is exactly the regression the architect found when probing a
/// brute-force-only partial file (the bootstrap admin failing
/// <c>VERIFY_PROFILE</c> with "Account is not fully set up"), so it must
/// already pass against today's bootstrap-created `master` and must still
/// pass, unmodified, once the import file exists.
/// </para>
///
/// <para>
/// New class rather than extending <see cref="BruteForceLockoutIntegrationTests"/>
/// (already 560 lines, ADR-0084 advisory 300 LOC/file). The master-admin
/// client, throwaway probe user and lock loop below are private copies
/// local to this file (plan.md §5) — <see cref="RealmProbe"/> is not widened
/// for a master-realm concern, and this file does not reach into
/// <see cref="BruteForceLockoutIntegrationTests"/>' own private helpers.
/// Each fact creates and deletes its own throwaway
/// <c>lockout-probe-&lt;guid&gt;</c> account in <b>master</b> — never
/// <c>admin</c> — so a locked probe can never poison the bootstrap admin
/// account the shared <see cref="AspireFixture"/> depends on, nor a sibling
/// fact in this collection.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MasterRealmBruteForceIntegrationTests(AspireFixture aspire)
{
    private const string MasterRealm = "master";

    // The master realm's bootstrap admin-cli account — the same
    // `KeycloakPassword` Aspire parameter every other AppHost-boot test in
    // this project already passes (AspireFixture.cs:315's
    // "Parameters:KeycloakPassword=testkeycloak"; wired to Keycloak's own
    // admin console at src/AppHost/AppHost.cs:30,144).
    private const string MasterRealmAdminUsername = "admin";
    private const string MasterRealmAdminPassword = "testkeycloak";

    private const string ProbePassword = "Lockout-Probe-Pw1";
    private const string WrongPassword = "wrong-on-purpose";

    // FR-002 — the seven fields "match the main realm" (spec.md Decision)
    // names beyond bruteForceProtected itself. Read live off both realms
    // below, never hard-coded, so a reviewer overturning either realm's
    // value never needs this test edited.
    private static readonly string[] MatchingFields =
    [
        "permanentLockout",
        "failureFactor",
        "waitIncrementSeconds",
        "maxFailureWaitSeconds",
        "maxDeltaTimeSeconds",
        "quickLoginCheckMilliSeconds",
        "minimumQuickLoginWaitSeconds",
    ];

    /// <summary>
    /// FR-001/FR-002 — the happy-path scenario (spec.md). Expected red on
    /// unpatched code: <c>master.bruteForceProtected</c> reads <c>false</c>
    /// (confirmed live, spec.md §Context: <c>"failureFactor":30</c> too).
    /// </summary>
    [Fact]
    public async Task The_master_realm_is_brute_force_protected_with_the_application_realms_settings()
    {
        using var admin = await MasterRealmAdminClientAsync(CancellationToken.None);

        var master = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{MasterRealm}", CancellationToken.None);
        var application = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}", CancellationToken.None);

        RequireProperty(master, "bruteForceProtected").GetBoolean().ShouldBeTrue(
            $"'{MasterRealm}' should be brute-force protected to match '{RealmProbe.Realm}'. "
            + $"master representation: {master}");

        foreach (var field in MatchingFields)
        {
            var masterValue = RequireProperty(master, field);
            var applicationValue = RequireProperty(application, field);

            masterValue.GetRawText().ShouldBe(
                applicationValue.GetRawText(),
                $"'{field}' should be the same on '{MasterRealm}' as on '{RealmProbe.Realm}' — "
                + $"master: {masterValue.GetRawText()}, application: {applicationValue.GetRawText()}.");
        }
    }

    /// <summary>
    /// FR-001/FR-003 — the conflict scenario (spec.md). Deliberately never
    /// interpolates the raw token-endpoint response body into a Shouldly
    /// failure message, mirroring
    /// <see cref="BruteForceLockoutIntegrationTests.The_correct_password_is_refused_after_too_many_wrong_ones"/> —
    /// if this fact goes red again, a 200 response body here is a live
    /// token pair for a master-realm account in a public repository.
    /// Expected red on unpatched code: the final, correct-password grant
    /// succeeds (status 200, carries an access_token).
    /// </summary>
    [Fact]
    public async Task A_master_realm_account_is_refused_its_correct_password_after_too_many_wrong_ones()
    {
        (var username, var id, var failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using var response = await RequestTokenAsync(username, ProbePassword, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(body);
            var hasAccessToken = document.RootElement.TryGetProperty("access_token", out _);
            var hasError = document.RootElement.TryGetProperty("error", out var error);
            var errorValue = hasError ? error.GetString() : null;

            response.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"after {failureFactor + 1} wrong password grants, the *correct* password for "
                + $"'{username}' (in '{MasterRealm}') should be refused by the realm's brute-force "
                + $"lockout, not accepted. status: {(int)response.StatusCode}, response carried an "
                + $"access_token: {hasAccessToken} (body withheld from this message on purpose — see "
                + "the doc comment on this fact).");
            hasAccessToken.ShouldBeFalse(
                "a refused grant must not carry an access_token (body withheld from this message).");
            hasError.ShouldBeTrue(
                "the refusal body should name an OAuth error (body withheld from this message).");
            errorValue.ShouldBe("invalid_grant", $"error reported: '{errorValue}'.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// The guard (plan.md §5 test 3) — green before and after, unmodified.
    /// This is what catches the naive-partial-file failure the architect
    /// found: a <c>master-realm.json</c> carrying only the brute-force
    /// fields breaks the bootstrap admin grant with
    /// <c>resolve_required_actions</c> / "Account is not fully set up",
    /// because an *imported* realm gets the standard new-realm user profile
    /// (email/firstName/lastName required) unless the file also carries the
    /// bootstrap user-profile component (plan.md §2).
    ///
    /// <para>
    /// Must pass on today's bootstrap-created <c>master</c> (no import file
    /// exists yet) and must still pass, unmodified, once
    /// <c>master-realm.json</c> exists — both the admin grant itself and
    /// <c>accessTokenLifespan</c> staying at Keycloak's bootstrap value of
    /// 60 (spec.md §Context: bootstrap default is 60; the realm-import
    /// default a careless file would fall back to is 300).
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_bootstrap_admin_still_authenticates_and_master_keeps_its_bootstrap_token_lifespan()
    {
        using var response = await RequestTokenAsync(
            MasterRealmAdminUsername, MasterRealmAdminPassword, CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the bootstrap admin's password grant against '{MasterRealm}' should keep succeeding; "
            + $"the shared AspireFixture depends on this account. status: {(int)response.StatusCode}");

        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("access_token", out _).ShouldBeTrue(
            "the bootstrap admin's grant should carry an access_token (body withheld from this "
            + "message — see BruteForceLockoutIntegrationTests for why raw grant bodies are never "
            + "logged).");

        using var admin = await MasterRealmAdminClientAsync(CancellationToken.None);
        var master = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{MasterRealm}", CancellationToken.None);

        RequireProperty(master, "accessTokenLifespan").GetInt32().ShouldBe(
            60,
            $"'{MasterRealm}' should report Keycloak's bootstrap accessTokenLifespan (60), not the "
            + $"realm-import default (300). representation: {master}");
    }

    /// <summary>
    /// A field missing from the master-realm representation is an
    /// escalation, not a default — same reasoning as
    /// <c>BruteForceLockoutIntegrationTests.ReadFailureFactorAsync</c>'s doc
    /// comment on why the master-admin token (not <c>identity-admin</c>,
    /// which has no <c>view-realm</c>) is used throughout this file.
    /// </summary>
    private static JsonElement RequireProperty(JsonElement representation, string propertyName)
    {
        representation.TryGetProperty(propertyName, out var value).ShouldBeTrue(
            $"a master-admin read of a realm representation did not include '{propertyName}' at "
            + $"all. representation: {representation}");
        return value;
    }

    /// <summary>
    /// Creates a throwaway account in <b>master</b>, locks it with
    /// <c>failureFactor + 1</c> wrong password grants (an upper bound, not a
    /// claim about which specific attempt did it —
    /// <c>quickLoginCheckMilliSeconds</c> can fire first, mirroring
    /// <c>BruteForceLockoutIntegrationTests.CreateAndLockProbeAsync</c>), and
    /// hands back the account plus master's own <c>failureFactor</c> so no
    /// assertion has to hard-code it.
    /// </summary>
    private async Task<(string Username, string Id, int FailureFactor)> CreateAndLockProbeAsync(
        CancellationToken cancellationToken)
    {
        (var username, var id) = await CreateProbeUserAsync(cancellationToken);
        var failureFactor = await ReadFailureFactorAsync(cancellationToken);

        for (var attempt = 1; attempt <= failureFactor + 1; attempt++)
        {
            using var wrong = await RequestTokenAsync(username, WrongPassword, cancellationToken);
            wrong.IsSuccessStatusCode.ShouldBeFalse(
                $"wrong-password grant #{attempt} of {failureFactor + 1} for '{username}' (in "
                + $"'{MasterRealm}') unexpectedly succeeded; the throwaway account's password should "
                + $"never be '{WrongPassword}'.");
        }

        return (username, id, failureFactor);
    }

    private async Task<int> ReadFailureFactorAsync(CancellationToken cancellationToken)
    {
        using var admin = await MasterRealmAdminClientAsync(cancellationToken);
        var representation = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{MasterRealm}", cancellationToken);

        return RequireProperty(representation, "failureFactor").GetInt32();
    }

    /// <summary>
    /// Mints a token for the master realm's bootstrap <c>admin</c> /
    /// <c>admin-cli</c> account — the one account confirmed (spec.md
    /// §Context, and <c>BruteForceLockoutIntegrationTests</c>'s own copy) to
    /// receive the <b>full</b> realm representation, <c>failureFactor</c>
    /// included.
    /// </summary>
    private async Task<HttpClient> MasterRealmAdminClientAsync(CancellationToken cancellationToken)
    {
        using var response = await RequestTokenAsync(
            MasterRealmAdminUsername, MasterRealmAdminPassword, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"minting a master-realm admin-cli token failed with {(int)response.StatusCode}; without "
            + $"it this file cannot read either realm's real configuration. body: {body}");

        using var document = JsonDocument.Parse(body);
        var token = document.RootElement.GetProperty("access_token").GetString()!;

        var admin = aspire.CreateKeycloakClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return admin;
    }

    /// <summary>
    /// A fresh, per-fact throwaway user created through the Admin API in
    /// <b>master</b>, never <c>admin</c>. Unlike
    /// <c>BruteForceLockoutIntegrationTests.CreateProbeUserAsync</c> (which
    /// must supply <c>firstName</c>/<c>lastName</c>/<c>email</c> for
    /// <c>smart-sentinel-eye</c>'s declarative User Profile),
    /// <c>master</c>'s bootstrap profile requires no attribute at all
    /// (plan.md §2 — this is the exact parity the guard fact protects), so
    /// <c>username</c>/<c>enabled</c> is enough both before and after the
    /// import file exists.
    /// </summary>
    private async Task<(string Username, string Id)> CreateProbeUserAsync(CancellationToken cancellationToken)
    {
        var username = $"lockout-probe-{Guid.NewGuid():N}";
        using var admin = await MasterRealmAdminClientAsync(cancellationToken);

        var created = await admin.PostAsJsonAsync(
            $"admin/realms/{MasterRealm}/users",
            new { username, enabled = true },
            cancellationToken);
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"creating throwaway probe user '{username}' in '{MasterRealm}' failed with "
            + $"{(int)created.StatusCode}; without it every assertion below proves nothing. body: "
            + $"{await created.Content.ReadAsStringAsync(cancellationToken)}");

        var location = created.Headers.Location!.ToString();
        var id = location[(location.LastIndexOf('/') + 1)..];

        // From here on the user exists in the realm even though setup has
        // not finished. If the reset-password call throws, delete on the
        // way out instead of leaking it the way #2166 leaked clients.
        try
        {
            var resetPassword = await admin.PutAsJsonAsync(
                $"admin/realms/{MasterRealm}/users/{id}/reset-password",
                new { type = "password", value = ProbePassword, temporary = false },
                cancellationToken);
            resetPassword.IsSuccessStatusCode.ShouldBeTrue(
                $"setting the throwaway probe's password failed with {(int)resetPassword.StatusCode}; "
                + $"body: {await resetPassword.Content.ReadAsStringAsync(cancellationToken)}");
        }
        catch
        {
            await DeleteProbeUserAsync(id, cancellationToken);
            throw;
        }

        return (username, id);
    }

    /// <summary>
    /// Status-checked, deliberately: an unchecked delete that answers
    /// 404/409/500 leaves a <c>lockout-probe-*</c> user — and its lock —
    /// behind in <b>master</b>, indistinguishable from a real finding by the
    /// next run's sweep (issue #2166's shape).
    /// </summary>
    private async Task DeleteProbeUserAsync(string id, CancellationToken cancellationToken)
    {
        using var admin = await MasterRealmAdminClientAsync(cancellationToken);
        var deleted = await admin.DeleteAsync(
            $"admin/realms/{MasterRealm}/users/{id}", cancellationToken);

        deleted.IsSuccessStatusCode.ShouldBeTrue(
            $"deleting throwaway probe user '{id}' from '{MasterRealm}' answered "
            + $"{(int)deleted.StatusCode}; it is still in the realm and the next pass over it will "
            + "read it as residue it did not create.");
    }

    /// <summary>
    /// A raw token-endpoint POST against <b>master</b>, deliberately not
    /// routed through
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/> —
    /// that helper throws on a non-success response, and every fact here
    /// needs to inspect the failure, not just detect one.
    /// </summary>
    private async Task<HttpResponseMessage> RequestTokenAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        using var keycloak = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = username,
            ["password"] = password,
        };

        return await keycloak.PostAsync(
            $"/realms/{MasterRealm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);
    }
}
