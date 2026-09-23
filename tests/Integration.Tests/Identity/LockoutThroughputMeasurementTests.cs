using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 219 Half B (issue #2510) — the rate half of "is the exhaustible
/// candidate set Half A found also cheap to try?" Half A
/// (<c>SeededCredentialStrengthTests</c>, <c>Architecture.Tests</c>) computed
/// the set from the realm file alone. This file measures, against the real,
/// unmodified stack, how many attempts per lock the realm's own brute-force
/// detector actually permits — burst and paced — and how long a lock takes to
/// clear on its own, because "well within the lockout's budget" (#2510) is
/// arithmetic nobody had run against the running server.
///
/// <para>
/// <b>It asserts structure and records numbers; it asserts no threshold.</b>
/// <see cref="A_paced_caller_is_admitted_more_attempts_than_a_bursting_one"/>
/// asserts a paced attacker is admitted strictly more attempts than a bursting
/// one, and <see cref="A_temporary_lock_expires_without_administrative_intervention"/>
/// asserts a lock eventually clears on its own. Neither asserts what the
/// numbers are — a threshold is a policy judgement, and ADR-0144 keeps the
/// autonomous lane out of making that one. The figures are written to test
/// output and multiplied into a wall-clock estimate in
/// <c>specs/219-the-guess-a-username-already-made/verification.md</c>.
/// </para>
///
/// <para>
/// <b>Excluded from CI by its category</b> (<c>Trait("Category","Measurement")</c>):
/// <see cref="A_temporary_lock_expires_without_administrative_intervention"/>
/// waits out a real lock — up to <c>maxFailureWaitSeconds</c> (900s) plus a
/// margin — and <c>IntegrationTestSelectionTests</c> derives the legal category
/// set from <c>ci.yml</c>'s own exclusion. A timeout in that fact is a failure,
/// not a skip.
/// </para>
///
/// <para>
/// <b>Never sends a wrong password to a seeded account</b> — every probe below
/// is <c>$"policy-probe-{Guid.NewGuid():N}"</c>, created and deleted inside the
/// fact that uses it, following spec 207 and spec 214's pattern. The one seeded
/// account this file touches (<see cref="AspireFixture.AdminUsername"/>, SC-9)
/// is read with its own correct password only, never guessed.
/// </para>
///
/// <para>
/// <b>Never <c>DELETE</c>s the attack-detection record.</b>
/// <c>BruteForceLockoutIntegrationTests</c> (spec 207) and
/// <c>LockoutSessionSurvivalIntegrationTests</c> (spec 214) both clear a probe's
/// lock explicitly, as their own cleanup path — the shortcut that means neither
/// file has ever observed how long a lock actually lasts.
/// <see cref="A_temporary_lock_expires_without_administrative_intervention"/> is
/// deliberately the first fact in this repository that waits one out instead.
/// </para>
///
/// <para>
/// Helpers are copied from <c>BruteForceLockoutIntegrationTests</c> and
/// <c>LockoutSessionSurvivalIntegrationTests</c> rather than promoted onto
/// <c>RealmProbe</c> — this is the third consumer, and plan.md §3.2 declines
/// the promotion anyway: <c>RealmProbe</c> is shared with
/// <c>KeycloakAdminTokenProviderTests</c> and <c>MqttAudienceIntegrationTests</c>,
/// neither of which touches users, passwords or attack detection, and widening
/// it here would edit a shared file mid-flight on two other worktrees for no
/// behaviour change. A follow-up issue (phase 7) names the now-three copies.
/// </para>
///
/// <para>
/// <b>Auth/scope, recorded rather than re-asserted.</b>
/// <see cref="The_running_realm_declares_the_password_policy_the_import_file_carries"/>
/// (SC-6) reads the realm representation through the master realm's bootstrap
/// <c>admin</c>/<c>admin-cli</c> account precisely because Keycloak's Admin API
/// already refuses a caller holding no realm-management role — not a guarantee
/// this file adds, but the reason <c>identity-admin</c> (no <c>view-realm</c>)
/// gets a silently partial representation instead of an outright refusal, and
/// why this class cannot read the realm back through it (spec 207 phase 6).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
[Trait("Category", "Measurement")]
public class LockoutThroughputMeasurementTests(AspireFixture aspire, ITestOutputHelper output)
{
    // Satisfies the realm's password policy (length(8) and upperCase(1) and
    // lowerCase(1) and digits(1)) with margin — same shape as
    // BruteForceLockoutIntegrationTests.ProbePassword.
    private const string ProbePassword = "Throughput-Probe-Pw1";
    private const string WrongPassword = "wrong-on-purpose";

    // Same credentials BruteForceLockoutIntegrationTests names identically.
    private const string MasterRealmAdminUsername = "admin";
    private const string MasterRealmAdminPassword = "testkeycloak";

    private const int PacingMarginMilliseconds = 100;
    private const int BurstAttemptCeiling = 50;
    private const int PacedAttemptCeilingMargin = 15;

    private static readonly TimeSpan LockExpiryPollInterval = TimeSpan.FromSeconds(5);

    // maxFailureWaitSeconds (900) plus a margin — a bound, not a claim about
    // when the lock actually clears. A timeout here is a failure (plan.md §5).
    private static readonly TimeSpan LockExpiryPollTimeout = TimeSpan.FromSeconds(1020);

    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// SC-5 — the control on the mint. Without this, every refusal recorded
    /// below is indistinguishable from a mistyped username, a bad client, or a
    /// broken stack.
    /// </summary>
    [Fact]
    public async Task A_fresh_probe_accounts_correct_password_mints_a_token()
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
    /// SC-6 — the running realm agrees with the file. Half A
    /// (<c>SeededCredentialStrengthTests</c>) reads only the file; this is the
    /// control that the import actually landed what the file declares, read
    /// through the master realm so a dropped field is an escalation rather
    /// than <c>identity-admin</c>'s silent partial read (spec 207 phase 6).
    /// </summary>
    [Fact]
    public async Task The_running_realm_declares_the_password_policy_the_import_file_carries()
    {
        string filePolicy = DeclaredPasswordPolicyFromRealmFile();
        JsonElement representation = await ReadRealmRepresentationAsync(CancellationToken.None);

        RequireProperty(representation, "passwordPolicy").GetString().ShouldBe(
            filePolicy,
            $"the running realm's declared passwordPolicy should match the import file's — otherwise "
            + "Half A's SeededCredentialStrengthTests is guarding a field the import silently dropped, "
            + "or the running server has since diverged from the file.");

        RequireProperty(representation, "failureFactor");
        RequireProperty(representation, "quickLoginCheckMilliSeconds");
    }

    /// <summary>
    /// SC-7a — the bursting side of SC-7's comparison, and its own control:
    /// the account genuinely reaches <c>disabled: true</c>, not merely a
    /// sequence of refused wrong guesses.
    /// </summary>
    [Fact]
    public async Task A_bursting_caller_is_refused_after_the_quick_login_check_trips()
    {
        (string username, string id, int attempts) = await BurstUntilLockedAsync(CancellationToken.None);
        try
        {
            output.WriteLine(
                $"burst: '{username}' reported disabled after {attempts} wrong-password attempt(s) "
                + "with no delay between them.");

            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
                admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", CancellationToken.None);
            output.WriteLine($"attack-detection record at lock: {attackDetection}");

            attackDetection.GetProperty("disabled").GetBoolean().ShouldBeTrue(
                $"'{username}' should still report disabled == true. record: {attackDetection}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-7 — the falsifiable claim this spec exists to check.
    ///
    /// <para>
    /// <b>Hypothesis, formed before the run and explicitly not the answer</b>
    /// (spec.md §SC-7): the bursting account trips
    /// <c>quickLoginCheckMilliSeconds</c> at a small number of failures while
    /// the paced account reaches closer to <c>failureFactor</c>. <b>Named
    /// falsifier:</b> if both lock at the same attempt count, that is a finding
    /// about spec 207's own conclusion, recorded in <c>verification.md</c>, not
    /// adjusted away here (ADR-0144 §4a).
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_paced_caller_is_admitted_more_attempts_than_a_bursting_one()
    {
        (string burstUsername, string burstId, int burstAttempts) = await BurstUntilLockedAsync(CancellationToken.None);
        try
        {
            // G2: the pacing interval that avoids the quick-login check is
            // quickLoginCheckMilliSeconds + a margin, read off the running
            // realm rather than hard-coded (spec.md §Assumptions). Read
            // together with failureFactor, one mint and one GET, rather than
            // each of this fact's two realm reads minting its own client.
            (int failureFactor, int quickLoginCheckMilliseconds) =
                await ReadLockoutConfigurationAsync(CancellationToken.None);
            TimeSpan pace = TimeSpan.FromMilliseconds(quickLoginCheckMilliseconds + PacingMarginMilliseconds);

            (string pacedUsername, string pacedId, int pacedAttempts, TimeSpan pacedElapsed) =
                await PacedUntilLockedAsync(pace, failureFactor, CancellationToken.None);
            try
            {
                output.WriteLine(
                    $"burst: {burstAttempts} attempt(s) ('{burstUsername}'); paced "
                    + $"(+{pace.TotalMilliseconds:F0} ms between attempts): {pacedAttempts} attempt(s) "
                    + $"('{pacedUsername}'), loop elapsed {pacedElapsed.TotalSeconds:F1}s (measured, "
                    + "Stopwatch).");

                pacedAttempts.ShouldBeGreaterThan(
                    burstAttempts,
                    $"a paced attacker (>{quickLoginCheckMilliseconds} ms between wrong guesses) should "
                    + $"be admitted strictly more attempts before lockout than a bursting one. burst: "
                    + $"{burstAttempts}, paced: {pacedAttempts}. If both locked at the same count, "
                    + "quickLoginCheckMilliSeconds is not doing what spec 207 concluded — record that "
                    + "finding, do not adjust this assertion (SC-7's named falsifier).");
            }
            finally
            {
                await DeleteProbeUserAsync(pacedId, CancellationToken.None);
            }
        }
        finally
        {
            await DeleteProbeUserAsync(burstId, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-8 — the measurement nobody has taken. Spec 207 and spec 214 both
    /// cleared a probe's lock through the Admin API rather than waiting it
    /// out, so <c>minimumQuickLoginWaitSeconds</c> (60s) and
    /// <c>maxFailureWaitSeconds</c> (900s) have never been observed against
    /// the running realm. This fact polls the correct password until it is
    /// accepted again and records the elapsed wall-clock time — it does not
    /// <c>DELETE</c> the attack-detection record.
    /// </summary>
    [Fact]
    public async Task A_temporary_lock_expires_without_administrative_intervention()
    {
        (string username, string id, int attempts) = await BurstUntilLockedAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            JsonElement attackDetectionAtLock = await RealmProbe.ReadJsonAsync(
                admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", CancellationToken.None);
            output.WriteLine(
                $"'{username}' locked after {attempts} burst attempt(s); polling every "
                + $"{LockExpiryPollInterval.TotalSeconds:F0}s for natural expiry (no DELETE of the "
                + $"attack-detection record). record at lock: {attackDetectionAtLock}");

            Stopwatch stopwatch = Stopwatch.StartNew();

            // Phase-6 re-review (spec 219): without this, a realm change that
            // stopped enforcing the lock at /token — while attack-detection
            // still reports disabled: true, which is all BurstUntilLockedAsync
            // checks — would pass this fact on its very first poll, reporting
            // a near-zero duration that SC-11's arithmetic would then divide
            // by. Proven by counterfactual against a never-locked probe.
            using (HttpResponseMessage firstPoll = await RequestTokenAsync(username, ProbePassword, CancellationToken.None))
            {
                firstPoll.StatusCode.ShouldNotBe(
                    HttpStatusCode.OK,
                    $"'{username}''s correct password was accepted on the very first poll, immediately "
                    + "after locking — the lock is not actually refusing sign-in at the token endpoint, "
                    + "only reported as disabled by the attack-detection record the burst helper checks. "
                    + "A near-zero duration here would be a finding about the lockout, not a passing "
                    + "measurement.");
            }

            output.WriteLine($"  still locked at {stopwatch.Elapsed.TotalSeconds:F0}s (confirmed refused on first poll)");

            bool accepted = false;
            while (stopwatch.Elapsed < LockExpiryPollTimeout)
            {
                await Task.Delay(LockExpiryPollInterval, CancellationToken.None);

                using HttpResponseMessage response = await RequestTokenAsync(username, ProbePassword, CancellationToken.None);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    accepted = true;
                    break;
                }

                output.WriteLine($"  still locked at {stopwatch.Elapsed.TotalSeconds:F0}s");
            }

            stopwatch.Stop();
            accepted.ShouldBeTrue(
                $"'{username}''s correct password was not accepted within "
                + $"{LockExpiryPollTimeout.TotalSeconds:F0}s of being locked; a lock that never clears "
                + "on its own is a finding, not a timeout to relax.");
            output.WriteLine(
                $"measured lock duration: {stopwatch.Elapsed.TotalSeconds:F1}s until '{username}''s "
                + "correct password was accepted again.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// SC-9 — the blast-radius control. Spec 207's SC-4 in a new file; also
    /// protects every other test in the <c>Aspire</c> collection — if this
    /// ever fails, this file's probes are locking the realm rather than an
    /// account.
    /// </summary>
    [Fact]
    public async Task A_seeded_account_authenticates_while_a_probe_is_locked()
    {
        (string username, string id, int attempts) = await BurstUntilLockedAsync(CancellationToken.None);
        try
        {
            using HttpResponseMessage response = await RequestTokenAsync(
                AspireFixture.AdminUsername, AspireFixture.AdminPassword, CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"'{AspireFixture.AdminUsername}' should authenticate normally while an unrelated probe "
                + $"('{username}', locked after {attempts} burst attempt(s)) is locked out — a "
                + $"realm-wide lockout would poison the rest of this test collection. status: "
                + $"{(int)response.StatusCode}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// A malformed request is not a failed login. Corrected in advance from
    /// spec 207's live finding: Keycloak 26.6.4 answers this shape with 401,
    /// not the RFC 6749 §5.2 400 the "invalid_request" body would suggest —
    /// the load-bearing claim is the <c>error</c> value, not the status code.
    ///
    /// <para>
    /// <b>Carries no probe and no counter check.</b> Phase-6 review (spec
    /// 219): an earlier version created a probe purely to run a counter
    /// check documented as unable to fail — a grant with no username at all
    /// cannot be attributed to any account, so the check was dead code by
    /// its own comment's admission. Per ADR-0036 and this repository's "five
    /// assertions that couldn't fail" lesson, the fix for a dead assertion is
    /// deletion, not a comment explaining why it is fine to keep. The
    /// falsifiable version of the counter claim — a malformed grant that
    /// <em>does</em> name a real account — is its own fact below,
    /// <see cref="A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login"/>,
    /// added when the attempted repair here (name the username, omit only the
    /// password) turned out to change Keycloak's own diagnosis
    /// (<c>invalid_grant</c>, not <c>invalid_request</c>) rather than fix
    /// this fact.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_grant_with_no_username_is_refused_as_invalid_request_not_invalid_grant()
    {
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "password",
            ["client_id"] = AspireFixture.ClientId,
            ["password"] = ProbePassword,
            ["scope"] = "openid",
        };
        using HttpResponseMessage response = await PostTokenFormAsync(form, CancellationToken.None);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            $"a grant with no username at all should be refused, not silently accepted. body: {body}");

        using JsonDocument document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("error", out JsonElement error).ShouldBeTrue(
            $"the refusal body should name an OAuth error. body: {body}");
        error.GetString().ShouldBe(
            "invalid_request",
            $"a missing username must be diagnosed as a bad request, not a lockout "
            + $"('invalid_grant' is what a lockout answers with). body: {body}");
    }

    /// <summary>
    /// What a grant naming a real account but missing its password actually
    /// does — added at phase-6 review (spec 219) as the honest fix for the
    /// "no username" fact's unfalsifiable counter check above, once the
    /// originally-proposed repair (name the username, omit the password,
    /// expect <c>invalid_request</c>) turned out to be wrong about Keycloak's
    /// real behaviour rather than merely untested.
    ///
    /// <para>
    /// Observed against the live server: <c>400</c> /
    /// <c>invalid_grant</c> / <c>"Invalid user credentials"</c> — the same
    /// shape as a genuinely wrong password, and the probe's own failure
    /// counter <b>does</b> move. In the one malformation actually measured
    /// here (username present, password entirely absent), Keycloak's
    /// structural, pre-authentication validation answered <c>invalid_request</c>
    /// only when the <em>username</em> was absent (the other fact above); a
    /// missing password with a present username was instead handled as an
    /// ordinary failed authentication attempt, not specially diagnosed as
    /// malformed. Not verified against other malformations (a blank-but-present
    /// password value, an absent <c>grant_type</c>, and so on) — the claim is
    /// scoped to what was actually sent. This is worth recording in its own
    /// right: a client bug that sends a password grant with an empty or
    /// absent password field is indistinguishable, server-side, from a wrong
    /// guess, and would count toward that account's lockout.
    /// </para>
    ///
    /// <para>
    /// Proven by counterfactual: with <c>failuresAfter.ShouldBe(failuresBefore)</c>
    /// (the wrong prediction) in place, this fact went red against the real
    /// server; corrected to <c>failuresBefore + 1</c>, it is the green below.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_grant_naming_a_real_account_but_missing_its_password_counts_as_a_failed_login()
    {
        (string username, string id) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            int failuresBefore = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);

            Dictionary<string, string> form = new()
            {
                ["grant_type"] = "password",
                ["client_id"] = AspireFixture.ClientId,
                ["username"] = username,
                ["scope"] = "openid",
            };
            using HttpResponseMessage response = await PostTokenFormAsync(form, CancellationToken.None);
            string body = await response.Content.ReadAsStringAsync();

            response.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"a grant naming '{username}' but missing its password should be refused as an ordinary "
                + $"failed authentication, not silently accepted. body: {body}");

            using JsonDocument document = JsonDocument.Parse(body);
            document.RootElement.TryGetProperty("error", out JsonElement error).ShouldBeTrue(
                $"the refusal body should name an OAuth error. body: {body}");
            error.GetString().ShouldBe(
                "invalid_grant",
                $"a present username with a missing password is handled as a failed authentication "
                + $"attempt, not diagnosed as a malformed request. body: {body}");

            int failuresAfter = await ReadAttackDetectionFailureCountAsync(admin, id, CancellationToken.None);
            failuresAfter.ShouldBe(
                failuresBefore + 1,
                $"'{username}''s failure counter should move by exactly one — Keycloak treats a missing "
                + $"password the same as a wrong one once a real username is present. before: "
                + $"{failuresBefore}, after: {failuresAfter}.");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// Sends wrong-password grants with no delay between them until the
    /// account's own attack-detection record reports <c>disabled: true</c>,
    /// and hands back how many attempts that took. Bounded at
    /// <see cref="BurstAttemptCeiling"/> — spec 207's own verification
    /// observed this trip at 2 failures via <c>quickLoginCheckMilliSeconds</c>,
    /// so the ceiling is a safety bound, not an expected value.
    ///
    /// <para>
    /// <b>The probe is deleted on any exception, not only by the caller's own
    /// <c>finally</c>.</b> Phase-6 review (spec 219): the ceiling assertion
    /// below throws from inside this helper, before <c>id</c> is ever
    /// returned — exactly the "lockout isn't tripping as configured" scenario
    /// this file exists to catch — so the caller's own
    /// <c>try { … } finally { DeleteProbeUserAsync(id) }</c> never starts, and
    /// a locked, failure-laden <c>policy-probe-*</c> account would otherwise
    /// leak into the shared realm on the exact failure path this file is
    /// supposed to detect. Proven by counterfactual (rig
    /// <see cref="BurstAttemptCeiling"/> to 1, confirm a probe survives
    /// without this catch, confirm none does with it).
    /// </para>
    /// </summary>
    private async Task<(string Username, string Id, int Attempts)> BurstUntilLockedAsync(
        CancellationToken cancellationToken)
    {
        (string username, string id) = await CreateProbeUserAsync(cancellationToken);

        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);

            int attempts = 0;
            bool disabled = false;
            while (!disabled && attempts < BurstAttemptCeiling)
            {
                attempts++;
                using HttpResponseMessage wrong = await RequestTokenAsync(username, WrongPassword, cancellationToken);
                wrong.IsSuccessStatusCode.ShouldBeFalse(
                    $"wrong-password grant #{attempts} for '{username}' unexpectedly succeeded; the "
                    + $"throwaway account's password should never be '{WrongPassword}'.");

                JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
                    admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", cancellationToken);
                disabled = attackDetection.GetProperty("disabled").GetBoolean();
            }

            disabled.ShouldBeTrue(
                $"'{username}' did not report disabled == true within {BurstAttemptCeiling} burst "
                + "attempts; either the lockout stopped tripping or the ceiling needs raising.");

            return (username, id, attempts);
        }
        catch
        {
            await DeleteProbeUserAsync(id, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Sends wrong-password grants spaced by <paramref name="pace"/> until
    /// the account's own attack-detection record reports <c>disabled: true</c>,
    /// and hands back how many attempts that took, plus the real wall-clock
    /// time the loop itself took (spec 219 phase-6 re-review, verification.md
    /// §SC-11 — that arithmetic previously sourced this figure from prose
    /// reasoning rather than a captured measurement). Bounded at
    /// <paramref name="failureFactor"/> plus
    /// <see cref="PacedAttemptCeilingMargin"/> — read once by the caller
    /// alongside <c>quickLoginCheckMilliSeconds</c> rather than minting a
    /// second master-realm client here for the same representation.
    ///
    /// <para>
    /// The probe is deleted on any exception, not only by the caller's own
    /// <c>finally</c> — same reasoning as <see cref="BurstUntilLockedAsync"/>;
    /// not separately exercised by counterfactual (the proof rigged
    /// <c>BurstAttemptCeiling</c>, not this method's own ceiling).
    /// </para>
    /// </summary>
    private async Task<(string Username, string Id, int Attempts, TimeSpan Elapsed)> PacedUntilLockedAsync(
        TimeSpan pace, int failureFactor, CancellationToken cancellationToken)
    {
        (string username, string id) = await CreateProbeUserAsync(cancellationToken);

        try
        {
            using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
            int ceiling = failureFactor + PacedAttemptCeilingMargin;

            Stopwatch stopwatch = Stopwatch.StartNew();
            int attempts = 0;
            bool disabled = false;
            while (!disabled && attempts < ceiling)
            {
                if (attempts > 0)
                {
                    await Task.Delay(pace, cancellationToken);
                }

                attempts++;
                using HttpResponseMessage wrong = await RequestTokenAsync(username, WrongPassword, cancellationToken);
                wrong.IsSuccessStatusCode.ShouldBeFalse(
                    $"wrong-password grant #{attempts} for '{username}' unexpectedly succeeded; the "
                    + $"throwaway account's password should never be '{WrongPassword}'.");

                JsonElement attackDetection = await RealmProbe.ReadJsonAsync(
                    admin, $"admin/realms/{RealmProbe.Realm}/attack-detection/brute-force/users/{id}", cancellationToken);
                disabled = attackDetection.GetProperty("disabled").GetBoolean();
            }

            stopwatch.Stop();

            disabled.ShouldBeTrue(
                $"'{username}' did not report disabled == true within {ceiling} paced attempts "
                + $"(realm failureFactor + {PacedAttemptCeilingMargin} margin); either the lockout stopped "
                + "tripping or the ceiling needs raising.");

            return (username, id, attempts, stopwatch.Elapsed);
        }
        catch
        {
            await DeleteProbeUserAsync(id, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Reads <c>passwordPolicy</c> directly off the realm import file — the
    /// same file-locating logic <c>SeededCredentialStrengthTests</c> uses in
    /// <c>Architecture.Tests</c>, duplicated rather than shared because the two
    /// projects share no reference and this is the only field this file needs
    /// from the file.
    /// </summary>
    private static string DeclaredPasswordPolicyFromRealmFile()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");

        string text = File.ReadAllText(path).TrimStart('﻿');
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("passwordPolicy").GetString() ?? string.Empty;
    }

    /// <summary>
    /// Reads the realm representation off the running server through the
    /// master realm's bootstrap <c>admin</c>/<c>admin-cli</c>, not
    /// <c>identity-admin</c>, which has no <c>view-realm</c> role and returns
    /// a silently partial representation (spec 207 phase 6). One mint, one
    /// GET, shared by every caller that needs a field off it — extracted at
    /// phase-6 re-review so a fact needing two fields (SC-7's
    /// <c>failureFactor</c> and <c>quickLoginCheckMilliSeconds</c>) reads the
    /// representation once rather than minting a client per field.
    /// </summary>
    private async Task<JsonElement> ReadRealmRepresentationAsync(CancellationToken cancellationToken)
    {
        using HttpClient admin = await MasterRealmAdminClientAsync(cancellationToken);
        return await RealmProbe.ReadJsonAsync(admin, $"admin/realms/{RealmProbe.Realm}", cancellationToken);
    }

    /// <summary>
    /// A field missing from the master-realm representation is an
    /// escalation, not a default — same reasoning throughout this file.
    /// </summary>
    private static JsonElement RequireProperty(JsonElement representation, string propertyName)
    {
        representation.TryGetProperty(propertyName, out JsonElement value).ShouldBeTrue(
            $"the master-realm admin-cli read of 'admin/realms/{RealmProbe.Realm}' did not include "
            + $"'{propertyName}' at all. representation: {representation}");
        return value;
    }

    /// <summary>
    /// <c>failureFactor</c> and <c>quickLoginCheckMilliSeconds</c> together,
    /// off one representation read — G2's pacing interval and
    /// <see cref="PacedUntilLockedAsync"/>'s ceiling both come from here,
    /// never hard-coded.
    /// </summary>
    private async Task<(int FailureFactor, int QuickLoginCheckMilliSeconds)> ReadLockoutConfigurationAsync(
        CancellationToken cancellationToken)
    {
        JsonElement representation = await ReadRealmRepresentationAsync(cancellationToken);
        return (
            RequireProperty(representation, "failureFactor").GetInt32(),
            RequireProperty(representation, "quickLoginCheckMilliSeconds").GetInt32());
    }

    /// <summary>
    /// Mints a token for the master realm's bootstrap <c>admin</c> /
    /// <c>admin-cli</c> account — the one account confirmed (spec 207) to
    /// receive the <b>full</b> realm representation, <c>passwordPolicy</c> and
    /// <c>failureFactor</c> included. Same credentials <c>AspireFixture.cs</c>
    /// wires every AppHost-boot test to (<c>KeycloakPassword=testkeycloak</c>),
    /// not <c>dev-only-keycloak-admin</c> (the <c>aspire run</c> default).
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
            + $"it this file cannot read the realm's real configuration. body: {body}");

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
    /// A fresh, per-fact throwaway user created through the Admin API — never
    /// a seeded account, never a fixture user. Mirrors
    /// <c>BruteForceLockoutIntegrationTests.CreateProbeUserAsync</c>: the
    /// realm's declarative User Profile requires <c>firstName</c>/
    /// <c>lastName</c>/<c>email</c> independent of <c>verifyEmail</c>, so a
    /// user missing any of them fails the token endpoint's
    /// <c>VERIFY_PROFILE</c> check with <c>invalid_grant</c> — which reads
    /// exactly like a lockout and would make every burst/paced fact above
    /// pass for the wrong reason.
    /// </summary>
    private async Task<(string Username, string Id)> CreateProbeUserAsync(CancellationToken cancellationToken)
    {
        string username = $"policy-probe-{Guid.NewGuid():N}";
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/users",
            new
            {
                username,
                enabled = true,
                firstName = "Throughput",
                lastName = "Probe",
                email = $"{username}@policy-probe.test",
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
    /// 404/409/500 leaves a <c>policy-probe-*</c> user, and its lock, behind —
    /// indistinguishable from a real finding by the next run's sweep
    /// (issue #2166's shape), and a leaked lock fails the next test class for
    /// a reason that reads exactly like an unrelated regression.
    /// </summary>
    private async Task DeleteProbeUserAsync(string id, CancellationToken cancellationToken)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(cancellationToken);
        HttpResponseMessage deleted = await admin.DeleteAsync(
            $"admin/realms/{RealmProbe.Realm}/users/{id}", cancellationToken);

        deleted.IsSuccessStatusCode.ShouldBeTrue(
            $"deleting throwaway probe user '{id}' answered {(int)deleted.StatusCode}; it is still in "
            + "the realm, and its lock with it, and the next pass over this realm will read it as "
            + "residue it did not create.");
    }

    /// <summary>
    /// A raw token-endpoint POST, deliberately not routed through
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/> —
    /// that helper throws on a non-success response, and every fact here needs
    /// to inspect the failure, not just detect one.
    /// </summary>
    private Task<HttpResponseMessage> RequestTokenAsync(
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

        return PostTokenFormAsync(form, cancellationToken);
    }

    /// <summary>
    /// Posts an arbitrary form to the token endpoint — the shared mechanism
    /// behind <see cref="RequestTokenAsync"/> and the two malformed-grant
    /// facts above, which each build a deliberately incomplete form rather
    /// than a valid username/password pair.
    /// </summary>
    private async Task<HttpResponseMessage> PostTokenFormAsync(
        Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using HttpClient keycloak = aspire.CreateKeycloakClient();
        return await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            cancellationToken);
    }
}
