using System.IO;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 325 (issue #2510) — AS-1, AS-2, AS-3. The dev realm's password
/// policy today (<c>length(8) and upperCase(1) and lowerCase(1) and
/// digits(1)</c>) admits a guess space the lockout's own budget (spec 207,
/// #2285) outlasts (spec 219). The fix raises it to <c>length(15) and
/// upperCase(1) and lowerCase(1) and digits(1) and specialChars(1) and
/// notContainsUsername and notEmail</c> (plan.md §2) — phase 4b, not yet
/// landed when this file is written.
///
/// <para>
/// <b>Colour (tasks.md phase 4a):</b>
/// <see cref="The_running_realm_refuses_a_password_the_old_policy_admitted"/>
/// (AS-1) and
/// <see cref="Each_new_policy_clause_refuses_a_password_that_only_breaks_it"/>
/// (AS-2) are <b>red</b> on unpatched code — the current policy accepts
/// every probe password below (204), so each "should be refused" assertion
/// fails. <see cref="The_running_realm_declares_exactly_what_the_import_file_declares"/>
/// (AS-3) is a <b>drift control, green before and after, unmodified</b>:
/// file and running realm must already agree on the declared
/// <c>passwordPolicy</c> string today, and must still agree once the string
/// changes. Spec 219's SC-6 checked this only in the CI-excluded
/// <c>Measurement</c> category; this class runs in CI (shard 2).
/// </para>
///
/// <para>
/// Reads and writes the running <b>application</b> realm
/// (<see cref="RealmProbe.Realm"/>) through a master-realm admin-cli token,
/// mirroring <c>MasterRealmBruteForceIntegrationTests</c> and
/// <c>BruteForceLockoutIntegrationTests.ReadFailureFactorAsync</c> — not
/// <see cref="RealmProbe.AuthorisedAdminClientAsync"/>'s
/// <c>identity-admin</c>, whose realm-management roles are
/// <c>manage-users</c>/<c>view-users</c> only (no <c>view-realm</c>), so a
/// realm-representation read through it comes back silently partial and
/// would make AS-3 pass for the wrong reason. Each fact creates and deletes
/// its own throwaway <c>policy-probe-&lt;guid&gt;</c> account — never
/// <c>operator</c> or <c>admin</c> — inside
/// <c>[Collection(AspireCollection.Name)]</c>, so it never contends with a
/// sibling fact or a seeded account.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class PasswordPolicyEnforcementIntegrationTests(AspireFixture aspire)
{
    // The master realm's bootstrap admin-cli account — the same
    // `KeycloakPassword` Aspire parameter every other AppHost-boot test in
    // this project already passes (AspireFixture.cs's
    // "Parameters:KeycloakPassword=testkeycloak"; wired to Keycloak's own
    // admin console at src/AppHost/AppHost.cs).
    private const string MasterRealmAdminUsername = "admin";
    private const string MasterRealmAdminPassword = "testkeycloak";

    // Compliant with both the current policy and spec 325's new one, with
    // margin, so a policy change trips a specific assertion below rather
    // than the probe's own setup (mirrors
    // BruteForceLockoutIntegrationTests.ProbePassword).
    private const string CompliantProbePassword = "Policy-Probe-Setup-9";

    // AS-1 — satisfies the OLD policy (length(8)+upperCase+lowerCase+digits:
    // 13 chars, has all three classes) but not the NEW one (no special
    // character).
    private const string OldPolicyOnlyPassword = "Probe1234Abcd";

    private const string Realm = "smart-sentinel-eye";

    /// <summary>
    /// AS-1 — the happy path of the fix. Red today: the current policy
    /// admits <see cref="OldPolicyOnlyPassword"/>, so the reset answers 204,
    /// not 400.
    /// </summary>
    [Fact]
    public async Task The_running_realm_refuses_a_password_the_old_policy_admitted()
    {
        (string username, string id, _) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await MasterRealmAdminClientAsync(CancellationToken.None);
            using HttpResponseMessage reset = await ResetPasswordAsync(admin, id, OldPolicyOnlyPassword, CancellationToken.None);

            reset.StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                $"resetting '{username}''s password to '{OldPolicyOnlyPassword}' (13 chars, "
                + "upper+lower+digit, no special — compliant with the old 'length(8) and "
                + "upperCase(1) and lowerCase(1) and digits(1)' policy) should be refused by the "
                + $"realm's current declared policy. status: {(int)reset.StatusCode}, body: "
                + $"{await reset.Content.ReadAsStringAsync()}");

            using HttpResponseMessage token = await RequestTokenAsync(username, CompliantProbePassword, CancellationToken.None);
            token.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"'{username}''s own compliant password should still mint a token after the refused "
                + $"reset above left it unchanged. status: {(int)token.StatusCode}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// AS-2 — each new clause refuses on its own. Three resets, each built
    /// to satisfy every clause of the new policy except the one it is named
    /// for, plus the control that the probe's own compliant password still
    /// mints a token after all three refusals. Red today: the current
    /// policy has no <c>specialChars</c>, <c>notContainsUsername</c> or
    /// <c>notEmail</c> clause, so all three resets answer 204.
    /// </summary>
    [Fact]
    public async Task Each_new_policy_clause_refuses_a_password_that_only_breaks_it()
    {
        (string username, string id, string email) = await CreateProbeUserAsync(CancellationToken.None);
        try
        {
            using HttpClient admin = await MasterRealmAdminClientAsync(CancellationToken.None);

            // 15 chars, upper+lower+digit, no special character.
            const string missingSpecial = "Abcdefghijklmn1";
            // Compliant on every other clause; contains the probe's own
            // username verbatim.
            string containsUsername = $"Xy9-{username}";
            // The probe's own email, verbatim — itself 15+ chars and
            // class-compliant (EmailOf), and built to not contain the
            // username, so this refusal isolates `notEmail` from
            // `notContainsUsername`.
            string equalsEmail = email;

            (string Password, string Reason)[] refusals =
            [
                (missingSpecial, "missing a special character"),
                (containsUsername, "containing its own username"),
                (equalsEmail, "equal to its own email, verbatim"),
            ];

            foreach ((string password, string reason) in refusals)
            {
                using HttpResponseMessage reset = await ResetPasswordAsync(admin, id, password, CancellationToken.None);

                reset.StatusCode.ShouldBe(
                    HttpStatusCode.BadRequest,
                    $"resetting '{username}''s password to a value {reason} should be refused by "
                    + $"the realm's declared policy. status: {(int)reset.StatusCode}, body: "
                    + $"{await reset.Content.ReadAsStringAsync()}");
            }

            using HttpResponseMessage token = await RequestTokenAsync(username, CompliantProbePassword, CancellationToken.None);
            token.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                $"'{username}''s own compliant password should still mint a token after all three "
                + $"refusals above left it unchanged. status: {(int)token.StatusCode}");
        }
        finally
        {
            await DeleteProbeUserAsync(id, CancellationToken.None);
        }
    }

    /// <summary>
    /// AS-3 — the drift control. Green before and after, unmodified: file
    /// and running realm must agree on <c>passwordPolicy</c> today (the old
    /// string) and must still agree once the string changes.
    /// </summary>
    [Fact]
    public async Task The_running_realm_declares_exactly_what_the_import_file_declares()
    {
        string declaredInFile = DeclaredPolicyInFile();

        using HttpClient admin = await MasterRealmAdminClientAsync(CancellationToken.None);
        JsonElement representation = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{Realm}", CancellationToken.None);

        representation.TryGetProperty("passwordPolicy", out JsonElement declaredOnServer).ShouldBeTrue(
            $"the master-admin read of 'admin/realms/{Realm}' did not include 'passwordPolicy' at "
            + $"all. representation: {representation}");

        declaredOnServer.GetString().ShouldBe(
            declaredInFile,
            "the running realm's declared 'passwordPolicy' should equal the import file's string, "
            + "verbatim.");
    }

    /// <summary>
    /// A random email that is class-compliant on its own (upper, lower,
    /// digit, special, 15+ chars) and does <b>not</b> embed
    /// <paramref name="username"/> — unlike <see cref="EmailOf"/>'s former
    /// shape, which happened to make every email contain its own username
    /// and so could never isolate <c>notEmail</c> from
    /// <c>notContainsUsername</c>.
    /// </summary>
    private static string EmailOf() => $"Policy-Mail-9-{Guid.NewGuid():N}@Policy.Test";

    /// <summary>
    /// A fresh, per-fact throwaway user created through the Admin API in
    /// <see cref="Realm"/>, never <c>operator</c>/<c>admin</c>. Mirrors
    /// <c>BruteForceLockoutIntegrationTests.CreateProbeUserAsync</c>:
    /// <c>firstName</c>/<c>lastName</c>/<c>email</c> are required by the
    /// realm's declarative User Profile independent of
    /// <c>verifyEmail: false</c>, so a user missing any of them fails
    /// Keycloak's <c>VERIFY_PROFILE</c> check at the token endpoint. Hands
    /// back the email it set, so <c>Each_new_policy_clause_refuses_a_password_that_only_breaks_it</c>
    /// can reuse the exact value rather than generating a second, different
    /// random one.
    /// </summary>
    private async Task<(string Username, string Id, string Email)> CreateProbeUserAsync(CancellationToken cancellationToken)
    {
        string username = $"policy-probe-{Guid.NewGuid():N}";
        string email = EmailOf();
        using HttpClient admin = await MasterRealmAdminClientAsync(cancellationToken);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{Realm}/users",
            new
            {
                username,
                enabled = true,
                firstName = "Policy",
                lastName = "Probe",
                email,
            },
            cancellationToken);
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"creating throwaway probe user '{username}' failed with {(int)created.StatusCode}; "
            + $"without it every assertion below proves nothing. body: "
            + $"{await created.Content.ReadAsStringAsync(cancellationToken)}");

        string location = created.Headers.Location!.ToString();
        string id = location[(location.LastIndexOf('/') + 1)..];

        try
        {
            HttpResponseMessage resetPassword = await admin.PutAsJsonAsync(
                $"admin/realms/{Realm}/users/{id}/reset-password",
                new { type = "password", value = CompliantProbePassword, temporary = false },
                cancellationToken);
            resetPassword.IsSuccessStatusCode.ShouldBeTrue(
                $"setting the throwaway probe's own compliant password failed with "
                + $"{(int)resetPassword.StatusCode}; body: "
                + $"{await resetPassword.Content.ReadAsStringAsync(cancellationToken)}");
        }
        catch
        {
            await DeleteProbeUserAsync(id, cancellationToken);
            throw;
        }

        return (username, id, email);
    }

    /// <summary>
    /// Status-checked, deliberately: an unchecked delete that answers
    /// 404/409/500 leaves a <c>policy-probe-*</c> user behind, indistinguishable
    /// from a real finding by the next pass's sweep (issue #2166's shape).
    /// </summary>
    private async Task DeleteProbeUserAsync(string id, CancellationToken cancellationToken)
    {
        using HttpClient admin = await MasterRealmAdminClientAsync(cancellationToken);
        HttpResponseMessage deleted = await admin.DeleteAsync($"admin/realms/{Realm}/users/{id}", cancellationToken);

        deleted.IsSuccessStatusCode.ShouldBeTrue(
            $"deleting throwaway probe user '{id}' answered {(int)deleted.StatusCode}; it is still "
            + "in the realm and the next pass over it will read it as residue it did not create.");
    }

    private static Task<HttpResponseMessage> ResetPasswordAsync(
        HttpClient admin, string id, string password, CancellationToken cancellationToken) =>
        admin.PutAsJsonAsync(
            $"admin/realms/{Realm}/users/{id}/reset-password",
            new { type = "password", value = password, temporary = false },
            cancellationToken);

    /// <summary>
    /// A raw token-endpoint POST against <see cref="Realm"/>, deliberately
    /// not routed through
    /// <see cref="AspireFixture.GetAccessTokenAsync(string, string, CancellationToken)"/> —
    /// that helper throws on a non-success response, and the control asserts
    /// the success case directly.
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
            $"/realms/{Realm}/protocol/openid-connect/token", new FormUrlEncodedContent(form), cancellationToken);
    }

    /// <summary>
    /// Mints a token for the master realm's bootstrap <c>admin</c> /
    /// <c>admin-cli</c> account — the one account confirmed (spec.md
    /// §Auth; <c>MasterRealmBruteForceIntegrationTests</c>'s own copy) to
    /// receive the <b>full</b> realm representation, <c>passwordPolicy</c>
    /// included, for any realm it asks about — not
    /// <c>RealmProbe.AuthorisedAdminClientAsync</c>'s <c>identity-admin</c>,
    /// whose realm-management roles carry no <c>view-realm</c>.
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
            "/realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(form), cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"minting a master-realm admin-cli token failed with {(int)response.StatusCode}; "
            + $"without it this file cannot read or write the application realm. body: {body}");

        using JsonDocument document = JsonDocument.Parse(body);
        string token = document.RootElement.GetProperty("access_token").GetString()!;

        HttpClient admin = aspire.CreateKeycloakClient();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return admin;
    }

    /// <summary>
    /// Reads the realm import's declared <c>passwordPolicy</c> straight off
    /// disk — the same repository-root walk
    /// <c>RealmImportMirrorTests.RepositoryRoot</c> and three
    /// <c>Architecture.Tests</c> guards already use (spec 248 §1 row 6), kept
    /// as its own copy per that file's own doc comment rather than shared
    /// across projects.
    /// </summary>
    private static string DeclaredPolicyInFile()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm import should be at {path}");

        string text = File.ReadAllText(path);
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("passwordPolicy").GetString() ?? string.Empty;
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
}
