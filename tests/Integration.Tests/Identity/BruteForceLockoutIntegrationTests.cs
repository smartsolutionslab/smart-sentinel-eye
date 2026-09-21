using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 207 (issue #2285) — the realm counts nothing today
/// (<c>bruteForceProtected: false</c>), so a known username can be guessed at
/// whatever rate the network allows, forever.
///
/// <para>
/// <b><see cref="The_correct_password_is_refused_after_too_many_wrong_ones"/>
/// (SC-1) is the reason this spec exists.</b> Before the realm change, Keycloak
/// counts nothing, so the correct password is accepted after any number of
/// prior wrong ones and this test fails.
/// </para>
///
/// <para>
/// Every other fact here is a control that rules out a specific way SC-1's
/// refusal could be lying:
/// <see cref="A_fresh_account_authenticates_with_its_correct_password"/>
/// (SC-2) rules out a broken mint, a bad client, or a mistyped credential;
/// <see cref="Keycloak_records_the_account_as_temporarily_disabled"/> (SC-3)
/// separates "locked out" from every other cause of <c>invalid_grant</c>;
/// <see cref="A_second_account_is_unaffected_while_the_first_is_locked"/>
/// (SC-4) is the blast-radius guard — a realm-wide lock would poison the rest
/// of this collection, not just this file;
/// <see cref="Clearing_the_lockout_restores_authentication"/> (SC-5) checks
/// that <c>permanentLockout: false</c> is true in practice, and doubles as
/// this file's own cleanup path for the account it locks.
/// </para>
///
/// <para>
/// Each fact creates and deletes its own throwaway account
/// (<c>lockout-probe-&lt;guid&gt;</c>) through the Admin API (plan.md §3) —
/// never <c>operator</c> or <c>admin</c> — so a locked-out probe can never
/// poison a sibling test in this collection. <see cref="RealmProbe"/>'s
/// constants are reused for realm/admin-client identity; the user and
/// attack-detection helpers stay local to this file rather than widening
/// <see cref="RealmProbe"/>, which <c>KeycloakAdminTokenProviderTests</c> and
/// <c>MqttAudienceIntegrationTests</c> also depend on.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class BruteForceLockoutIntegrationTests(AspireFixture aspire)
{
    private const string SecondAccountUsername = AspireFixture.AdminUsername;
    private const string SecondAccountPassword = AspireFixture.AdminPassword;

    // Satisfies the realm's password policy (length(8) and upperCase(1) and
    // lowerCase(1) and digits(1)) with margin, so a policy change trips the
    // reset-password assertion in CreateProbeUserAsync, not a mysterious 400
    // deep inside a later assertion.
    private const string ProbePassword = "Lockout-Probe-Pw1";
    private const string WrongPassword = "wrong-on-purpose";

    // The master realm's bootstrap admin-cli account, not identity-admin —
    // see MasterRealmAdminClientAsync for why. Password is the same
    // `KeycloakPassword` Aspire parameter every other AppHost-boot test in
    // this project already passes (AspireFixture.cs:279's
    // "Parameters:KeycloakPassword=testkeycloak"; wired to Keycloak's own
    // admin console at src/AppHost/AppHost.cs:30,144).
    private const string MasterRealmAdminUsername = "admin";
    private const string MasterRealmAdminPassword = "testkeycloak";

    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// SC-2 — the control on the mint. Without this, SC-1's refusal is
    /// indistinguishable from a mistyped username, a bad client, or a broken
    /// stack.
    /// </summary>
    [Fact]
    public async Task A_fresh_account_authenticates_with_its_correct_password()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await RequestTokenAsync(username, ProbePassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "the positive control failed: a freshly-created account's own correct password was "
                + "refused before a single wrong guess. This is an escalation, not the feature under "
                + $"test — stop and report rather than treating it as expected red. body: "
                + $"{await response.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-1 — the reason this spec exists.
    ///
    /// <para>
    /// Deliberately never interpolates the raw response body into a Shouldly
    /// failure message here. This repository is public and `tasks.md` T007
    /// mandates quoting a phase-4 red's verbatim output in the PR body — if
    /// this fact ever goes red again (a realm regression), a 200 response
    /// body is a complete, live token pair. The assertions below read the
    /// parsed shape (status, whether an access token came back, the error
    /// code) and the failure messages report only that shape, never the
    /// bytes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_correct_password_is_refused_after_too_many_wrong_ones()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await RequestTokenAsync(username, ProbePassword, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(body);
            bool hasAccessToken = document.RootElement.TryGetProperty("access_token", out _);
            bool hasError = document.RootElement.TryGetProperty("error", out JsonElement error);
            string? errorValue = hasError ? error.GetString() : null;

            response.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"after {failureFactor + 1} wrong password grants, the *correct* password for "
                + $"'{username}' should be refused by the realm's brute-force lockout, not accepted. "
                + $"status: {(int)response.StatusCode}, response carried an access_token: "
                + $"{hasAccessToken} (body withheld from this message on purpose — see the doc "
                + "comment on this fact).");
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
    /// SC-3 — the control on the diagnosis. Separates "locked out" from "the
    /// password changed", "the account was disabled", "the client was
    /// rejected" — every other cause of <c>invalid_grant</c>.
    ///
    /// <para>
    /// <c>quickLoginCheckMilliSeconds: 1000</c> locks an account after two
    /// failures inside one second, independently of <c>failureFactor</c>, so a
    /// tight loop of wrong guesses can trip the quick-login check before the
    /// failure-factor counter reaches its configured value. <c>numFailures</c>
    /// is therefore asserted only as <c>&gt;= 1</c> and reported in the failure
    /// message — never asserted at <c>&gt;= failureFactor</c>, which would fail
    /// on exactly the realm this test is written to prove.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Keycloak_records_the_account_as_temporarily_disabled()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
                admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", CancellationToken.None);

            bool disabled = attackDetection.GetProperty("disabled").GetBoolean();
            int numFailures = attackDetection.GetProperty("numFailures").GetInt32();

            disabled.ShouldBeTrue(
                $"Keycloak's attack-detection record for '{username}' should report the account as "
                + $"temporarily disabled after too many wrong guesses. numFailures reported: "
                + $"{numFailures} (realm failureFactor: {failureFactor}). record: {attackDetection}");
            numFailures.ShouldBeGreaterThanOrEqualTo(
                1,
                $"the attack-detection record should show at least one recorded failure for "
                + $"'{username}'. numFailures reported: {numFailures} (realm failureFactor: {failureFactor}).");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-4 — the blast-radius guard. Brute-force protection is per-account; a
    /// future change that accidentally made it realm-wide would be caught here
    /// rather than surfacing as a mystery outage across the rest of this
    /// collection.
    /// </summary>
    [Fact]
    public async Task A_second_account_is_unaffected_while_the_first_is_locked()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await RequestTokenAsync(
                SecondAccountUsername, SecondAccountPassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"'{SecondAccountUsername}' should authenticate normally while an unrelated account "
                + $"('{username}', locked after {failureFactor + 1} wrong guesses) is locked out — a "
                + $"realm-wide lockout would poison the rest of this test collection. body: "
                + $"{await response.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-5 — recovery. <c>permanentLockout: false</c> is a claim about
    /// recovery; this is the claim being checked, via an explicit
    /// <c>DELETE</c> of the attack-detection record rather than a
    /// <c>Task.Delay</c> until the lock expires.
    ///
    /// <para>
    /// Asserts the account is <b>still locked before</b> the <c>DELETE</c>,
    /// so the <c>DELETE</c> is load-bearing. Without that assertion this fact
    /// cannot distinguish "recovery worked" from "the account was never
    /// locked in the first place" — on unpatched <c>develop</c> the throwaway
    /// account is never locked, the <c>DELETE</c> still answers <c>204</c>
    /// against a record that already reads clear, and the correct password
    /// still succeeds regardless.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Clearing_the_lockout_restores_authentication()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using (HttpResponseMessage stillLocked = await RequestTokenAsync(username, ProbePassword, CancellationToken.None))
            {
                stillLocked.StatusCode.ShouldBe(
                    HttpStatusCode.BadRequest,
                    $"'{username}' should still be refused its correct password before the lockout's "
                    + $"attack-detection record is cleared — otherwise the DELETE below cannot be shown "
                    + $"to have done anything. status: {(int)stillLocked.StatusCode}");
            }

            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            HttpResponseMessage cleared = await admin.DeleteAsync(
                $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", CancellationToken.None);
            cleared.IsSuccessStatusCode.ShouldBeTrue(
                $"clearing the attack-detection record for '{username}' answered "
                + $"{(int)cleared.StatusCode}; without it this test cannot tell recovery from a lock "
                + "that happened to have already expired.");

            using HttpResponseMessage response = await RequestTokenAsync(username, ProbePassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"after {failureFactor + 1} wrong guesses ('{username}') and an explicit DELETE of the "
                + "attack-detection record, the correct password should be accepted again. body: "
                + $"{await response.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// A malformed request is not a failed login. Pinned so a later tightening
    /// of the realm cannot quietly start counting parse errors against real
    /// accounts.
    ///
    /// <para>
    /// The malformed grant carries no username, so it was never "attempted"
    /// against the probe account in any sense Keycloak could record — reading
    /// the attack-detection record only *after* the grant proves nothing,
    /// because there is nothing that could plausibly have moved it. The real
    /// claim ("a bad request doesn't move the counter") needs a before/after
    /// comparison, so the failure count is read both sides of the malformed
    /// grant. Routed through <see cref="RealmProbe.ReadJsonAsync"/>, which
    /// throws on a non-success status rather than silently skipping the
    /// check the way an <c>if (response.IsSuccessStatusCode)</c> guard would.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_grant_with_no_username_is_a_bad_request_not_a_lockout()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            int failuresBefore = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);

            using HttpClient keycloak = aspire.CreateKeycloakClient();
            Dictionary<string, string> form = new()
            {
                ["grant_type"] = "password",
                ["client_id"] = AspireFixture.ClientId,
                ["password"] = ProbePassword,
                ["scope"] = "openid",
            };
            using HttpResponseMessage response = await keycloak.PostAsync(
                $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token",
                new FormUrlEncodedContent(form),
                CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            // Keycloak 26.6.4 answers this specific shape with 401, not the
            // RFC 6749 §5.2 400 the "invalid_request" body would suggest —
            // verified against the real, unmodified realm rather than assumed
            // from the spec. The load-bearing distinction is the OAuth `error`
            // value below: "invalid_request" (a malformed request) is never
            // "invalid_grant" (what a lockout answers with).
            response.StatusCode.ShouldBe(
                HttpStatusCode.Unauthorized,
                $"a grant with no username at all should be refused, not silently accepted. body: {body}");

            using JsonDocument document = JsonDocument.Parse(body);
            document.RootElement.TryGetProperty("error", out JsonElement error).ShouldBeTrue(
                $"the refusal body should name an OAuth error. body: {body}");
            error.GetString().ShouldBe(
                "invalid_request",
                $"a missing username must be diagnosed as a bad request, not a lockout ('invalid_grant' "
                + $"is what a lockout answers with). body: {body}");

            int failuresAfter = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);
            failuresAfter.ShouldBe(
                failuresBefore,
                $"a malformed grant carrying no username must not move '{username}''s failure counter. "
                + $"before: {failuresBefore}, after: {failuresAfter}.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// Creates a throwaway account, locks it with <c>failureFactor + 1</c>
    /// wrong password grants (an upper bound, not a claim about which specific
    /// attempt did it — <c>quickLoginCheckMilliSeconds</c> can fire first, see
    /// <see cref="Keycloak_records_the_account_as_temporarily_disabled"/>), and
    /// hands back the account plus the realm's own <c>failureFactor</c> so no
    /// assertion has to hard-code it.
    /// </summary>
    private async Task<(string Username, string Id, int FailureFactor)> CreateAndLockProbeAsync(
        CancellationToken cancellationToken)
    {
        (string username, string id) = await CreateProbeUserAsync(cancellationToken);
        int failureFactor = await ReadFailureFactorAsync(cancellationToken);

        for (int attempt = 1; attempt <= failureFactor + 1; attempt++)
        {
            using HttpResponseMessage wrong = await RequestTokenAsync(username, WrongPassword, cancellationToken);
            wrong.IsSuccessStatusCode.ShouldBeFalse(
                $"wrong-password grant #{attempt} of {failureFactor + 1} for '{username}' unexpectedly "
                + "succeeded; the throwaway account's password should never be "
                + $"'{WrongPassword}'.");
        }

        return (username, id, failureFactor);
    }

    /// <summary>
    /// Reads <c>failureFactor</c> off the running server rather than
    /// hard-coding it, so a reviewer overturning the realm's value
    /// (spec.md §Assumptions 3) never needs this test edited.
    ///
    /// <para>
    /// Deliberately reads through <see cref="MasterRealmAdminClientAsync"/>,
    /// not <c>RealmProbe.AuthorisedAdminClientAsync</c> (<c>identity-admin</c>).
    /// <c>identity-admin</c>'s realm-management roles are
    /// <c>manage-users</c>/<c>view-users</c> only — no <c>view-realm</c>
    /// (<c>smart-sentinel-eye-realm.json:582-591</c>) — so a
    /// <c>GET admin/realms/{realm}</c> through it comes back a silently
    /// *partial* representation that omits <c>failureFactor</c> entirely:
    /// confirmed live, <c>{"realm":...,"bruteForceProtected":true,
    /// "supportedLocales":[]}</c>. A previous version of this method treated
    /// that omission as "the field was never set" and fell back to
    /// Keycloak's built-in default of 30 — silently wrong, since the realm's
    /// real value is 10, and every wrong-guess loop in this file ran 31
    /// attempts instead of 11 without failing, because
    /// <c>quickLoginCheckMilliSeconds: 1000</c> locks the account after two
    /// rapid failures regardless of <c>failureFactor</c>. There is no silent
    /// fallback here now: a missing <c>failureFactor</c> in the master-admin
    /// response is treated as an escalation, not a default.
    /// </para>
    /// </summary>
    private async Task<int> ReadFailureFactorAsync(CancellationToken cancellationToken)
    {
        using HttpClient admin = await MasterRealmAdminClientAsync(cancellationToken);
        JsonElement representation = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}", cancellationToken);

        representation.TryGetProperty("failureFactor", out JsonElement failureFactor).ShouldBeTrue(
            $"the master-realm admin-cli read of 'admin/realms/{RealmProbe.Realm}' did not include "
            + "'failureFactor' at all — this path exists specifically to avoid identity-admin's "
            + $"silent, partial read (no view-realm role), so a missing field here is a real finding, "
            + $"not something to default around. representation: {representation}");
        return failureFactor.GetInt32();
    }

    /// <summary>
    /// Mints a token for the master realm's bootstrap <c>admin</c> /
    /// <c>admin-cli</c> account — the one account confirmed live to receive
    /// the *full* realm representation, <c>failureFactor</c> included. Used
    /// only by <see cref="ReadFailureFactorAsync"/>. Deliberately not a
    /// change to <c>identity-admin</c>'s realm-management roles: granting it
    /// <c>view-realm</c> would be a real permission escalation on a service
    /// account the Identity API uses in production to create/rotate Keycloak
    /// clients, for a test-only need.
    /// </summary>
    private async Task<HttpClient> MasterRealmAdminClientAsync(CancellationToken cancellationToken)
    {
        using HttpClient tokenClient = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = MasterRealmAdminUsername,
            ["password"] = MasterRealmAdminPassword,
        };

        using HttpResponseMessage response = await tokenClient.PostAsync(
            "/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"minting a master-realm admin-cli token failed with {(int)response.StatusCode}; without "
            + $"it ReadFailureFactorAsync cannot read the realm's real failureFactor. body: {body}");

        using JsonDocument document = JsonDocument.Parse(body);
        string token = document.RootElement.GetProperty("access_token").GetString()!;

        HttpClient admin = aspire.CreateKeycloakClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return admin;
    }

    /// <summary>
    /// Reads just <c>numFailures</c> off the attack-detection record, through
    /// <see cref="RealmProbe.ReadJsonAsync"/> so a non-success status throws
    /// rather than being silently skipped.
    /// </summary>
    private static async Task<int> ReadAttackDetectionFailureCountAsync(
        HttpClient admin, string id, CancellationToken cancellationToken)
    {
        JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", cancellationToken);
        return attackDetection.GetProperty("numFailures").GetInt32();
    }

    /// <summary>
    /// Plan.md §3's chosen shape: a fresh, per-run throwaway user created
    /// through the Admin API rather than a seeded account
    /// (<c>operator</c>/<c>admin</c>) or a fixture user added to the realm
    /// import. Never joins a group — nothing here calls a fab-scoped endpoint.
    ///
    /// <para>
    /// <c>firstName</c>/<c>lastName</c>/<c>email</c> are not optional here,
    /// despite the realm's <c>verifyEmail: false</c>: the realm's declarative
    /// User Profile (<c>GET admin/realms/{realm}/users/profile</c>) marks all
    /// three <c>required</c> for the <c>user</c> role, independent of
    /// <c>verifyEmail</c>. A user missing any of them fails Keycloak's
    /// dynamic <c>VERIFY_PROFILE</c> check at the token endpoint with
    /// <c>invalid_grant</c> / "Account is not fully set up" — discovered when
    /// the positive control (SC-2) failed with exactly that error against a
    /// user created with only <c>{ username, enabled: true }</c>.
    /// </para>
    /// </summary>
    private async Task<(string Username, string Id)> CreateProbeUserAsync(CancellationToken cancellationToken)
    {
        string username = $"lockout-probe-{Guid.NewGuid():N}";
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/users",
            new
            {
                username,
                enabled = true,
                firstName = "Lockout",
                lastName = "Probe",
                email = $"{username}@lockout-probe.test",
            },
            cancellationToken);
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"creating throwaway probe user '{username}' failed with {(int)created.StatusCode}; "
            + $"without it every assertion below proves nothing. body: "
            + $"{await created.Content.ReadAsStringAsync(cancellationToken)}");

        string location = created.Headers.Location!.ToString();
        string id = location[(location.LastIndexOf('/') + 1)..];

        // From here on the user exists in the realm even though setup has
        // not finished. If the reset-password call throws — a network
        // failure, or the assertion below — the id is never returned to the
        // caller's `finally`, so nothing deletes it. Delete it here on the
        // way out instead of leaking it the way #2166 leaked clients.
        try
        {
            HttpResponseMessage resetPassword = await admin.PutAsJsonAsync(
                $"admin/realms/{RealmProbe.Realm}/users/{id}/reset-password",
                new { type = "password", value = ProbePassword, temporary = false },
                cancellationToken);
            resetPassword.IsSuccessStatusCode.ShouldBeTrue(
                $"setting the throwaway probe's password failed with {(int)resetPassword.StatusCode}; "
                + "the realm's password policy is 'length(8) and upperCase(1) and lowerCase(1) and "
                + $"digits(1)' — a policy change should fail here, at setup, not at a downstream "
                + $"assertion. body: {await resetPassword.Content.ReadAsStringAsync(cancellationToken)}");
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
    /// 404/409/500 leaves a <c>lockout-probe-*</c> user behind, indistinguishable
    /// from a real finding by the next run's sweep — issue #2166's shape.
    /// </summary>
    private async Task DeleteProbeUserAsync(string id, CancellationToken cancellationToken)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
        HttpResponseMessage deleted = await admin.DeleteAsync(
            $"admin/realms/{RealmProbe.Realm}/users/{id}", cancellationToken);

        deleted.IsSuccessStatusCode.ShouldBeTrue(
            $"deleting throwaway probe user '{id}' answered {(int)deleted.StatusCode}; it is still in "
            + "the realm and the next pass over it will read it as residue it did not create.");
    }

    /// <summary>
    /// A raw token-endpoint POST, deliberately not routed through
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/> —
    /// that helper throws on a non-success response, and every fact here needs
    /// to inspect the failure, not just detect one.
    /// </summary>
    private async Task<HttpResponseMessage> RequestTokenAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = AspireFixture.ClientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        };

        return await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);
    }
}
