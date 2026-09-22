using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 214 (issue #2509) — spec 207 (#2285) turned on Keycloak's brute-force
/// detector and left open whether a locked-out account's *existing* session
/// survives via <c>grant_type=refresh_token</c>, i.e. whether the lockout
/// denies only new sign-ins or also revokes live sessions. This file puts
/// that question to the real, unmodified realm.
///
/// <para>
/// <b><see cref="A_locked_out_account_can_still_spend_its_refresh_token"/>
/// (SC-1) is the reason this spec exists.</b> The assertion is written for the
/// predicted (benign) outcome — a source read of Keycloak's refresh path found
/// no brute-force check on it. <b>A red here is not a defect to adjust away —
/// it is the severe finding</b>, and per ADR-0144 this file may not be edited
/// to make it pass.
/// </para>
///
/// <para>
/// Every other fact here is a control that rules out a specific way SC-1's
/// success could be lying:
/// <see cref="A_fresh_account_is_issued_both_an_access_token_and_a_refresh_token"/>
/// (SC-3) proves the pair was mintable in the first place — without it a
/// refused refresh would be indistinguishable from a client that never issues
/// one;
/// <see cref="The_locked_out_account_is_refused_its_correct_password_in_the_same_window"/>
/// (SC-2) is the endpoint-level control that the *password* grant, not just
/// the refresh grant, is refused for a locked account — <c>LockProbeAsync</c>
/// itself already asserts <c>disabled == true</c> on SC-1's own account
/// before SC-1 ever spends its refresh token, so a successful refresh is not
/// indistinguishable from a lock that never tripped;
/// <see cref="A_second_account_is_unaffected_while_the_first_is_locked"/>
/// (SC-5) is the blast-radius guard — a realm-wide lock would poison the rest
/// of this collection, not just this file.
/// <see cref="A_disabled_account_cannot_spend_its_refresh_token"/> (SC-4) is
/// the counterfactual — it proves the instrument can register a refusal at
/// all, via a mechanism genuinely different from a brute-force lockout.
/// </para>
///
/// <para>
/// Deliberately a new file rather than a sixth fact in
/// <c>BruteForceLockoutIntegrationTests</c> (spec 207): that file pins that
/// *authentication* stops; this one pins what *refresh* does, a different
/// endpoint grant, a different Keycloak code path, and a different failure to
/// guard against. The user and token-reading helpers stay local to this file
/// rather than widening <see cref="RealmProbe"/>, for the same reason spec 207
/// gives for its own local helpers — <c>KeycloakAdminTokenProviderTests</c>
/// and <c>MqttAudienceIntegrationTests</c> also depend on it, for the benefit
/// of exactly one caller here.
/// </para>
///
/// <para>
/// Each fact creates and deletes its own throwaway account
/// (<c>lockout-session-probe-&lt;guid&gt;</c>) through the Admin API — never
/// <c>operator</c>, <c>admin</c>, or any <c>wall-*</c> account — so a
/// locked-out probe can never poison a sibling test in this collection.
/// SC-5's second account is <see cref="AspireFixture.AdminUsername"/>, read
/// only, never locked.
/// </para>
///
/// <para>
/// <b>Secrets discipline.</b> This file's happy path (SC-1, SC-3) mints and
/// spends live token pairs, and this is a public repository. Every assertion
/// below reads status, <c>error</c>, and token presence/difference — never
/// token text — and no failure message interpolates a response body that
/// could carry one.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class LockoutSessionSurvivalIntegrationTests(AspireFixture aspire)
{
    // Satisfies the realm's password policy (length(8) and upperCase(1) and
    // lowerCase(1) and digits(1)) with margin — same shape as
    // BruteForceLockoutIntegrationTests.ProbePassword.
    private const string ProbePassword = "Session-Probe-Pw1";
    private const string WrongPassword = "wrong-on-purpose";

    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// SC-3 — the control on the mint. Without this, a refused refresh in
    /// <see cref="A_locked_out_account_can_still_spend_its_refresh_token"/>
    /// would be indistinguishable from a client that never issues a refresh
    /// token in the first place, or a broken stack.
    /// </summary>
    [Fact]
    public async Task A_fresh_account_is_issued_both_an_access_token_and_a_refresh_token()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await PostPasswordGrantAsync(
                username, ProbePassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"minting a fresh token pair for '{username}' should succeed before anything has "
                + $"locked it. status: {(int)response.StatusCode}");

            (string accessToken, string refreshToken) = await ReadTokenPairAsync(response, CancellationToken.None);

            accessToken.ShouldNotBeNullOrWhiteSpace(
                $"the token response for '{username}' should carry a non-empty access_token.");
            refreshToken.ShouldNotBeNullOrWhiteSpace(
                $"the token response for '{username}' should carry a non-empty refresh_token — "
                + "SC-1 asks what a lockout does to this token, so a client that never issues one "
                + "would make that question unanswerable.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-1 — the reason this spec exists. See the class doc comment: a red
    /// here is the severe finding, not a defect to adjust away.
    ///
    /// <para>
    /// Deliberately never interpolates a raw response body into a Shouldly
    /// failure message here, for the same reason
    /// <c>BruteForceLockoutIntegrationTests.The_correct_password_is_refused_after_too_many_wrong_ones</c>
    /// does not: this is a public repository and both branches of this fact's
    /// outcome involve a live token — the happy path's <c>200</c> carries a
    /// fresh pair, and a red result is quoted verbatim in a PR body per
    /// ADR-0144. The assertions below read the parsed shape only.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_locked_out_account_can_still_spend_its_refresh_token()
    {
        (string username, string id, string refreshToken, int failureFactor) =
            await CreateProbeUserLockedWithLiveRefreshTokenAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await PostRefreshGrantAsync(refreshToken, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(body);
            bool hasAccessToken = document.RootElement.TryGetProperty("access_token", out JsonElement accessTokenElement);
            bool hasError = document.RootElement.TryGetProperty("error", out JsonElement error);
            string? errorValue = hasError ? error.GetString() : null;
            string? newAccessToken = hasAccessToken ? accessTokenElement.GetString() : null;

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"after {failureFactor + 1} wrong password grants locked '{username}' out, its "
                + "existing refresh token should still mint a new access token — the predicted "
                + "(benign) branch of spec 214/#2509. A 400 here is the severe finding, not a test "
                + $"to fix (ADR-0144): the lockout would be revoking live sessions, not only refusing "
                + $"new sign-ins. status: {(int)response.StatusCode}, error reported: "
                + $"'{errorValue}' (body withheld from this message on purpose — see the doc "
                + "comment on this fact).");
            hasAccessToken.ShouldBeTrue(
                "a successful refresh grant must carry an access_token (body withheld from this "
                + "message).");
            newAccessToken.ShouldNotBeNullOrWhiteSpace(
                "the refreshed access_token must be non-empty.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-2 — the control on the lock. Without this, SC-1's success is
    /// indistinguishable from a lock that never tripped, already expired, or
    /// landed on a different user. Runs in the same window as SC-1, against an
    /// equivalently-locked account.
    /// </summary>
    [Fact]
    public async Task The_locked_out_account_is_refused_its_correct_password_in_the_same_window()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await PostPasswordGrantAsync(
                username, ProbePassword, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(body);
            bool hasError = document.RootElement.TryGetProperty("error", out JsonElement error);
            string? errorValue = hasError ? error.GetString() : null;

            response.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"after {failureFactor + 1} wrong password grants, the *correct* password for "
                + $"'{username}' should still be refused — otherwise SC-1's refresh success would "
                + $"prove nothing about a locked account. status: {(int)response.StatusCode} (body "
                + "withheld from this message on purpose — see this fact's doc comment).");
            hasError.ShouldBeTrue(
                "the refusal body should name an OAuth error (body withheld from this message).");
            errorValue.ShouldBe("invalid_grant", $"error reported: '{errorValue}'.");

            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
                admin,
                $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}",
                CancellationToken.None);

            bool disabled = attackDetection.GetProperty("disabled").GetBoolean();
            int numFailures = attackDetection.GetProperty("numFailures").GetInt32();

            disabled.ShouldBeTrue(
                $"Keycloak's attack-detection record for '{username}' should report the account as "
                + $"temporarily disabled in the same window SC-1 spends its refresh token. "
                + $"numFailures reported: {numFailures} (realm failureFactor: {failureFactor}). "
                + $"record: {attackDetection}");
            // quickLoginCheckMilliSeconds: 1000 can trip before failureFactor's own
            // count is reached (spec 207's own spec.md got this wrong live: it reads
            // 2, not failureFactor), so this asserts only that at least one failure
            // was recorded — never >= failureFactor.
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
    /// SC-5 — the blast-radius guard. Also protects the rest of the suite: if
    /// this fails, this file is poisoning every other integration test in the
    /// collection. The second account is <see cref="AspireFixture.AdminUsername"/>
    /// — read only, never locked, following spec 207's own
    /// <c>A_second_account_is_unaffected_while_the_first_is_locked</c>.
    /// </summary>
    [Fact]
    public async Task A_second_account_is_unaffected_while_the_first_is_locked()
    {
        (string username, string id, int failureFactor) = await CreateAndLockProbeAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await PostPasswordGrantAsync(
                AspireFixture.AdminUsername, AspireFixture.AdminPassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"'{AspireFixture.AdminUsername}' should authenticate normally while an unrelated "
                + $"account ('{username}', locked after {failureFactor + 1} wrong guesses) is locked "
                + $"out — a realm-wide lockout would poison the rest of this test collection. "
                + $"status: {(int)response.StatusCode}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-4 — the counterfactual, and the load-bearing scenario of the whole
    /// file (T003, spec 214/#2509). <see cref="A_locked_out_account_can_still_spend_its_refresh_token"/>
    /// (SC-1) asserts a *success*, so on its own it would pass just as happily
    /// against a Keycloak that ignores lockouts, or refresh tokens, entirely.
    /// This fact drives the same endpoint with the same kind of token down a
    /// genuinely different mechanism — <c>PUT .../users/{id}</c> with
    /// <c>enabled: false</c>, which Keycloak's own refresh path refuses via
    /// <c>TokenManager.validateToken</c>'s <c>if (!user.isEnabled())</c>
    /// branch, not the attack-detection branch SC-1 exercises — and proves the
    /// instrument registers a refusal when there is one to register.
    ///
    /// <para>
    /// The user is re-enabled before it is deleted, in a nested <c>finally</c>
    /// so neither cleanup step can swallow the other's failure: if re-enabling
    /// throws, the outer <c>finally</c> still attempts the delete, because a
    /// deleted user cannot stay disabled.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_disabled_account_cannot_spend_its_refresh_token()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            string refreshToken = await MintRefreshTokenAsync(username, CancellationToken.None);

            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            try
            {
                HttpResponseMessage disabled = await admin.PutAsJsonAsync(
                    $"admin/realms/{RealmProbe.Realm}/users/{id}",
                    new { enabled = false },
                    CancellationToken.None);
                disabled.IsSuccessStatusCode.ShouldBeTrue(
                    $"disabling throwaway probe user '{username}' failed with "
                    + $"{(int)disabled.StatusCode}; without it this fact is not exercising the "
                    + "branch it claims to.");

                using HttpResponseMessage response = await PostRefreshGrantAsync(refreshToken, CancellationToken.None);
                string body = await response.Content.ReadAsStringAsync();

                using JsonDocument document = JsonDocument.Parse(body);
                bool hasError = document.RootElement.TryGetProperty("error", out JsonElement error);
                string? errorValue = hasError ? error.GetString() : null;

                response.StatusCode.ShouldBe(
                    HttpStatusCode.BadRequest,
                    $"a refresh token belonging to a disabled account ('{username}') must be "
                    + "refused — this is the counterfactual that proves SC-1's assertion can "
                    + $"actually fail: a 200 here would mean the instrument is inert. status: "
                    + $"{(int)response.StatusCode} (body withheld from this message on purpose — "
                    + "see the class doc comment on secrets discipline).");
                hasError.ShouldBeTrue(
                    "the refusal body should name an OAuth error (body withheld from this "
                    + "message).");
                errorValue.ShouldBe("invalid_grant", $"error reported: '{errorValue}'.");
            }
            finally
            {
                HttpResponseMessage reenabled = await admin.PutAsJsonAsync(
                    $"admin/realms/{RealmProbe.Realm}/users/{id}",
                    new { enabled = true },
                    CancellationToken.None);
                reenabled.IsSuccessStatusCode.ShouldBeTrue(
                    $"re-enabling throwaway probe user '{username}' failed with "
                    + $"{(int)reenabled.StatusCode}; it must not be left disabled even though it "
                    + "is about to be deleted.");
            }
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// A corrupted refresh token is not a failed login. Pinned so a later
    /// tightening of the realm cannot quietly start counting refresh failures
    /// against real accounts — which would turn a renewing wall into its own
    /// attacker (spec.md §*Bad request*).
    ///
    /// <para>
    /// <b>Attributable to the probe, not a bare string.</b> A syntactically
    /// invalid token like <c>"not-a-real-refresh-token"</c> names no user at
    /// all, so a before/after count on *any* account's failure counter would
    /// pass vacuously — the counter could not have moved regardless of what
    /// Keycloak does with malformed grants, because Keycloak has nothing to
    /// attribute the attempt to. This mints a real pair for the probe first,
    /// then flips the last character of the refresh token's signature segment
    /// — still a three-segment JWT naming this probe's session in its
    /// (unverified but readable) payload, but one whose signature check must
    /// fail. That is what makes the before/after comparison on <i>this</i>
    /// account's own record a claim that could actually fail.
    /// </para>
    ///
    /// <para>
    /// <c>numFailures</c> is read both sides of the corrupted grant, routed
    /// through <see cref="RealmProbe.ReadJsonAsync"/> so a non-success status
    /// throws rather than being silently skipped — same shape as
    /// <c>BruteForceLockoutIntegrationTests.A_grant_with_no_username_is_a_bad_request_not_a_lockout</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_malformed_refresh_token_is_refused_without_moving_the_failure_counter()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            string refreshToken = await MintRefreshTokenAsync(username, CancellationToken.None);
            string corruptedToken = CorruptSignature(refreshToken);

            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            int failuresBefore = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);

            using HttpResponseMessage response = await PostRefreshGrantAsync(corruptedToken, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(body);
            bool hasError = document.RootElement.TryGetProperty("error", out JsonElement error);
            string? errorValue = hasError ? error.GetString() : null;

            response.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"a refresh token with a corrupted signature should be refused, not accepted. "
                + $"status: {(int)response.StatusCode} (body withheld from this message on purpose).");
            hasError.ShouldBeTrue(
                "the refusal body should name an OAuth error (body withheld from this message).");
            errorValue.ShouldBe("invalid_grant", $"error reported: '{errorValue}'.");

            int failuresAfter = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);
            failuresAfter.ShouldBe(
                failuresBefore,
                $"a corrupted refresh token must not move '{username}''s failure counter — a "
                + $"renewing session must never be able to count itself into a lockout. "
                + $"before: {failuresBefore}, after: {failuresAfter}.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// Flips the last character of a three-segment JWT's signature, leaving
    /// its header and payload — the parts naming the session and subject —
    /// byte-identical and readable. A signature-only corruption is what makes
    /// the token attributable but invalid, as opposed to a bare string that
    /// fails to parse as a JWT at all.
    /// </summary>
    private static string CorruptSignature(string jwt)
    {
        string[] segments = jwt.Split('.');
        segments.Length.ShouldBe(3, $"a refresh token should be a three-segment JWT; got {segments.Length} segment(s).");

        string signature = segments[2];
        signature.ShouldNotBeNullOrEmpty("a refresh token's signature segment should not be empty.");
        char lastCharacter = signature[^1];
        char flipped = lastCharacter == 'A' ? 'B' : 'A';

        return $"{segments[0]}.{segments[1]}.{signature[..^1]}{flipped}";
    }

    /// <summary>
    /// Creates a throwaway account, mints its token pair while it is still
    /// unlocked, then locks it with <c>failureFactor + 1</c> wrong password
    /// grants and hands back the refresh token minted *before* the lock. This
    /// is SC-1's own setup: the refresh token under test has to predate the
    /// lockout, or the fact would not be asking the question it claims to.
    /// </summary>
    private async Task<(string Username, string Id, string RefreshToken, int FailureFactor)>
        CreateProbeUserLockedWithLiveRefreshTokenAsync(CancellationToken cancellationToken)
    {
        (string username, string id) = await CreateProbeUserAsync(cancellationToken);
        string refreshToken = await MintRefreshTokenAsync(username, cancellationToken);
        int failureFactor = await LockProbeAsync(username, id, cancellationToken);

        return (username, id, refreshToken, failureFactor);
    }

    /// <summary>
    /// Mints a fresh token pair for an already-created probe user and hands
    /// back only the refresh token — the one field neither
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/>
    /// nor <c>FetchAccessTokenAsync</c> keeps. Extracted from
    /// <see cref="CreateProbeUserLockedWithLiveRefreshTokenAsync"/> so
    /// <see cref="A_disabled_account_cannot_spend_its_refresh_token"/> (SC-4)
    /// can mint a live refresh token without also locking the account — SC-4's
    /// whole point is a mechanism genuinely different from a lockout.
    /// </summary>
    private async Task<string> MintRefreshTokenAsync(string username, CancellationToken cancellationToken)
    {
        using HttpResponseMessage mint = await PostPasswordGrantAsync(username, ProbePassword, cancellationToken);
        mint.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"minting a token pair for '{username}' failed with {(int)mint.StatusCode}; without it "
            + "there is no refresh token to spend.");
        (_, string refreshToken) = await ReadTokenPairAsync(mint, cancellationToken);
        return refreshToken;
    }

    /// <summary>
    /// Creates a throwaway account and locks it with <c>failureFactor + 1</c>
    /// wrong password grants (an upper bound, not a claim about which specific
    /// attempt did it — <c>quickLoginCheckMilliSeconds</c> can fire first, same
    /// as spec 207's <c>CreateAndLockProbeAsync</c>).
    /// </summary>
    private async Task<(string Username, string Id, int FailureFactor)> CreateAndLockProbeAsync(
        CancellationToken cancellationToken)
    {
        (string username, string id) = await CreateProbeUserAsync(cancellationToken);
        int failureFactor = await LockProbeAsync(username, id, cancellationToken);
        return (username, id, failureFactor);
    }

    /// <summary>
    /// Locks the probe account, then reads <b>this account's own</b>
    /// attack-detection record and asserts <c>disabled == true</c> before
    /// returning — not just that each wrong-password grant was refused.
    ///
    /// <para>
    /// Without this, <see cref="A_locked_out_account_can_still_spend_its_refresh_token"/>
    /// (SC-1) never establishes that the account whose refresh token it
    /// spends was actually locked at that moment — the only account this file
    /// confirmed as locked would be <see cref="The_locked_out_account_is_refused_its_correct_password_in_the_same_window"/>'s
    /// (SC-2) own, separate probe. A refresh token that still works is only
    /// the finding this spec exists to report if the account holding it was
    /// genuinely disabled, not merely subjected to wrong guesses that may not
    /// have tripped the detector (a realm edit to <c>failureFactor</c> or
    /// <c>quickLoginCheckMilliSeconds</c>, a Keycloak upgrade, or a
    /// <c>bruteForceProtected</c> regression could all make this loop run
    /// with nothing actually locking).
    /// </para>
    /// </summary>
    private async Task<int> LockProbeAsync(string username, string id, CancellationToken cancellationToken)
    {
        int failureFactor = await ReadFailureFactorAsync(cancellationToken);

        for (int attempt = 1; attempt <= failureFactor + 1; attempt++)
        {
            using HttpResponseMessage wrong = await PostPasswordGrantAsync(username, WrongPassword, cancellationToken);
            wrong.IsSuccessStatusCode.ShouldBeFalse(
                $"wrong-password grant #{attempt} of {failureFactor + 1} for '{username}' unexpectedly "
                + $"succeeded; the throwaway account's password should never be '{WrongPassword}'.");
        }

        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
        JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", cancellationToken);
        attackDetection.GetProperty("disabled").GetBoolean().ShouldBeTrue(
            $"'{username}' should report disabled == true after {failureFactor + 1} wrong password "
            + $"grants — every fact that calls LockProbeAsync depends on the account it locked "
            + $"actually being locked, not merely having been sent wrong guesses. record: {attackDetection}");

        return failureFactor;
    }

    /// <summary>
    /// Reads <c>failureFactor</c> off the running server rather than
    /// hard-coding it — same reasoning and same route (the master realm's
    /// bootstrap <c>admin</c>/<c>admin-cli</c> account, not
    /// <c>identity-admin</c>, which has no <c>view-realm</c> role and returns
    /// a silently partial representation) as
    /// <c>BruteForceLockoutIntegrationTests.ReadFailureFactorAsync</c>.
    /// </summary>
    private async Task<int> ReadFailureFactorAsync(CancellationToken cancellationToken)
    {
        using HttpClient admin = await MasterRealmAdminClientAsync(cancellationToken);
        JsonElement representation = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}", cancellationToken);

        representation.TryGetProperty("failureFactor", out JsonElement failureFactor).ShouldBeTrue(
            $"the master-realm admin-cli read of 'admin/realms/{RealmProbe.Realm}' did not include "
            + "'failureFactor' at all — a missing field here is a real finding, not something to "
            + $"default around. representation: {representation}");
        return failureFactor.GetInt32();
    }

    /// <summary>
    /// Mints a token for the master realm's bootstrap <c>admin</c> /
    /// <c>admin-cli</c> account — the one account confirmed (spec 207) to
    /// receive the *full* realm representation, <c>failureFactor</c> included.
    /// Same credentials <c>AspireFixture.cs</c> wires every AppHost-boot test
    /// to (<c>KeycloakPassword=testkeycloak</c>).
    /// </summary>
    private async Task<HttpClient> MasterRealmAdminClientAsync(CancellationToken cancellationToken)
    {
        using HttpClient tokenClient = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = "admin",
            ["password"] = "testkeycloak",
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

    private static async Task<int> ReadAttackDetectionFailureCountAsync(
        HttpClient admin, string id, CancellationToken cancellationToken)
    {
        JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", cancellationToken);
        return attackDetection.GetProperty("numFailures").GetInt32();
    }

    /// <summary>
    /// Plan.md §*US1*'s chosen shape: a fresh, per-run throwaway user created
    /// through the Admin API rather than a seeded account. Never joins a
    /// group — nothing here calls a fab-scoped endpoint. Mirrors
    /// <c>BruteForceLockoutIntegrationTests.CreateProbeUserAsync</c>: the realm's
    /// declarative User Profile requires <c>firstName</c>/<c>lastName</c>/
    /// <c>email</c> independent of <c>verifyEmail</c>, so a user missing any of
    /// them fails the token endpoint's <c>VERIFY_PROFILE</c> check with
    /// <c>invalid_grant</c> rather than what the fact under test is checking.
    /// </summary>
    private async Task<(string Username, string Id)> CreateProbeUserAsync(CancellationToken cancellationToken)
    {
        string username = $"lockout-session-probe-{Guid.NewGuid():N}";
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/users",
            new
            {
                username,
                enabled = true,
                firstName = "Lockout",
                lastName = "SessionProbe",
                email = $"{username}@lockout-session-probe.test",
            },
            cancellationToken);
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"creating throwaway probe user '{username}' failed with {(int)created.StatusCode}; "
            + $"without it every assertion below proves nothing. body: "
            + $"{await created.Content.ReadAsStringAsync(cancellationToken)}");

        string location = created.Headers.Location!.ToString();
        string id = location[(location.LastIndexOf('/') + 1)..];

        // From here on the user exists in the realm even though setup has not
        // finished. If the reset-password call throws, delete on the way out
        // rather than leaking it the way #2166 leaked clients.
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
    /// 404/409/500 leaves a <c>lockout-session-probe-*</c> user behind,
    /// indistinguishable from a real finding by the next run's sweep —
    /// issue #2166's shape.
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
    /// A raw <c>grant_type=password</c> POST, deliberately not routed through
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/> —
    /// that helper throws on a non-success response, and every fact here needs
    /// to inspect the failure, not just detect one.
    /// </summary>
    private Task<HttpResponseMessage> PostPasswordGrantAsync(
        string username, string password, CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = AspireFixture.ClientId,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid",
        };
        return PostTokenAsync(form, cancellationToken);
    }

    /// <summary>
    /// A raw <c>grant_type=refresh_token</c> POST — this file's actual
    /// subject. <c>AspireFixture</c> has no refresh-token helper at all: it
    /// discards <c>refresh_token</c> when parsing a password grant's response
    /// (<c>FetchAccessTokenAsync</c>), and a repo-wide grep over <c>tests/</c>
    /// for <c>refresh_token</c> returns zero hits before this file.
    /// </summary>
    private Task<HttpResponseMessage> PostRefreshGrantAsync(
        string refreshToken, CancellationToken cancellationToken)
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = AspireFixture.ClientId,
            ["refresh_token"] = refreshToken,
        };
        return PostTokenAsync(form, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostTokenAsync(
        IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        return await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);
    }

    /// <summary>
    /// Parses a token response into just the two fields this file cares
    /// about, and <b>fails loudly if <c>refresh_token</c> is absent</b> rather
    /// than returning an empty string — an absent refresh token would make
    /// <see cref="A_locked_out_account_can_still_spend_its_refresh_token"/>
    /// (SC-1) pass vacuously, which is exactly the failure mode
    /// <see cref="A_disabled_account_cannot_spend_its_refresh_token"/> (SC-4)
    /// exists to rule out.
    /// </summary>
    private static async Task<(string AccessToken, string RefreshToken)> ReadTokenPairAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("access_token", out JsonElement accessTokenElement).ShouldBeTrue(
            "the token response did not carry an access_token at all (body withheld from this "
            + "message on purpose — this is a public repository and a 200 response here is a live "
            + "token pair).");
        document.RootElement.TryGetProperty("refresh_token", out JsonElement refreshTokenElement).ShouldBeTrue(
            "the token response did not carry a refresh_token at all — failing loudly here rather "
            + "than defaulting to an empty string, because an absent refresh token would make SC-1 "
            + "pass vacuously (body withheld from this message on purpose).");

        return (accessTokenElement.GetString()!, refreshTokenElement.GetString()!);
    }
}
