using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Authentication;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 440 T-H (ADR-0186 Decisions 1, 2, 5; AS-10, AS-11, AS-13, AS-14) — the credential
/// and the confinement, asked of the <b>running provider</b>.
///
/// <para>
/// The realm file can say <c>client-jwt</c> and a NEGATIVE policy; only Keycloak can say
/// that an assertion mints a <c>manage-realm</c> token, that the former secret does not,
/// and that the live Identity credential can neither read, rewrite nor remove the client.
/// Both measured reconfigure breaks (ADR-0186 Context) are tried here as
/// <c>identity-admin</c>, with its own declared credential, and must answer 403.
/// </para>
///
/// <para>
/// <b>State.</b> On a realm where the confinement does not hold these requests succeed, so
/// every test that can mutate migration-runner snapshots it as the master admin first and
/// puts it back in <c>finally</c> (re-creating it and re-granting its roles if a delete
/// went through). Otherwise a failing run would leave the client rewritten for the rest of
/// the fixture's lifetime and every later test would fail for a reason unrelated to its own.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class MigrationRunnerConfinementIntegrationTests(AspireFixture aspire)
{
    private const string FormerSecret = "dev-only-migration-runner-secret";

    private readonly RealmProbe realm = new(aspire);

    // ---- AS-10 / AS-11: the credential ----

    [Fact]
    public async Task A_signed_assertion_mints_a_token_that_holds_manage_realm()
    {
        var privateKey = await MigrationRunnerCredential.PrivateKeyAsync();

        var token = await MintSignedAsync(privateKey);

        RealmManagementRoles(token).ShouldContain("manage-realm");
    }

    [Fact]
    public async Task The_former_client_secret_no_longer_mints_a_token()
    {
        using var keycloak = aspire.CreateKeycloakClient();
        Dictionary<string, string> form = new()
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = MigrationRunnerCredential.ClientIdentifier,
            ["client_secret"] = FormerSecret,
        };

        using var response = await keycloak.PostAsync(
            $"/realms/{RealmProbe.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(form),
            CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().ShouldBe("invalid_client");
    }

    // ---- AS-13: identity-admin cannot rewrite migration-runner into a credential it can use ----

    [Fact]
    public async Task Identity_admin_cannot_register_its_own_public_key_and_mint_with_it()
    {
        var snapshot = await SnapshotMigrationRunnerAsync();
        using var attacker = RSA.Create(2048);

        try
        {
            using var identityAdmin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            var tampered = WithAttribute(snapshot.Representation, "jwks.string", JwksFor(attacker));

            using var response = await identityAdmin.PutAsync(
                $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}",
                Json(tampered),
                CancellationToken.None);

            response.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                "rewriting migration-runner's registered key is the first measured break (ADR-0186 Context).");
            await Should.ThrowAsync<HttpRequestException>(
                () => MintSignedAsync(Convert.ToBase64String(attacker.ExportPkcs8PrivateKey())));
        }
        finally
        {
            await RestoreMigrationRunnerAsync(snapshot);
        }
    }

    [Fact]
    public async Task Identity_admin_cannot_switch_the_client_back_to_a_secret_and_regenerate_it()
    {
        var snapshot = await SnapshotMigrationRunnerAsync();

        try
        {
            using var identityAdmin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            var tampered = JsonNode.Parse(snapshot.Representation)!;
            tampered["clientAuthenticatorType"] = "client-secret";

            using var switched = await identityAdmin.PutAsync(
                $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}",
                Json(tampered.ToJsonString()),
                CancellationToken.None);
            using var regenerated = await identityAdmin.PostAsync(
                $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}/client-secret",
                content: null,
                CancellationToken.None);

            switched.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the second measured break: change the authenticator.");
            regenerated.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "and then mint a secret it can read back.");
            (await ReadAuthenticatorTypeAsMasterAsync(snapshot)).ShouldBe("client-jwt");
        }
        finally
        {
            await RestoreMigrationRunnerAsync(snapshot);
        }
    }

    // ---- AS-14: identity-admin cannot see or remove migration-runner ----

    [Fact]
    public async Task Identity_admin_cannot_read_the_client_read_its_secret_or_delete_it()
    {
        var snapshot = await SnapshotMigrationRunnerAsync();

        try
        {
            using var identityAdmin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
            var path = $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}";

            using var read = await identityAdmin.GetAsync(path, CancellationToken.None);
            using var secret = await identityAdmin.GetAsync($"{path}/client-secret", CancellationToken.None);
            using var deleted = await identityAdmin.DeleteAsync(path, CancellationToken.None);

            read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            secret.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            deleted.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await ReadAuthenticatorTypeAsMasterAsync(snapshot)).ShouldNotBeNullOrEmpty(
                "migration-runner must still exist after the delete was refused.");
        }
        finally
        {
            await RestoreMigrationRunnerAsync(snapshot);
        }
    }

    [Fact]
    public async Task Identity_admin_does_not_find_the_client_in_a_listing_or_a_lookup_by_name()
    {
        using var identityAdmin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        var listing = await RealmProbe.ReadJsonAsync(
            identityAdmin, $"admin/realms/{RealmProbe.Realm}/clients?max=1000", CancellationToken.None);
        var lookup = await RealmProbe.ReadJsonAsync(
            identityAdmin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={MigrationRunnerCredential.ClientIdentifier}",
            CancellationToken.None);

        listing.EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString())
            .ShouldNotContain(MigrationRunnerCredential.ClientIdentifier);
        lookup.GetArrayLength().ShouldBe(0);
    }

    // ---- helpers ----

    private async Task<string> MintSignedAsync(string privateKeyBase64)
    {
        using var keycloak = aspire.CreateKeycloakClient();
        using ClientCredentialsTokenProvider tokens = new(
            new FakeHttpClientFactory(keycloak),
            "migration-runner-confinement-probe",
            () => new SignedJwtClientCredentials(
                keycloak.BaseAddress!.ToString(),
                RealmProbe.Realm,
                MigrationRunnerCredential.ClientIdentifier,
                privateKeyBase64),
            TimeProvider.System,
            NullLogger.Instance);

        return await tokens.GetAccessTokenAsync(CancellationToken.None);
    }

    private static IReadOnlyList<string> RealmManagementRoles(string accessToken)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(accessToken.Split('.')[1]));

        return [.. payload.RootElement
            .GetProperty("resource_access")
            .GetProperty("realm-management")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetString() ?? string.Empty)];
    }

    private static string JwksFor(RSA key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var modulus = Base64Url.EncodeToString(parameters.Modulus!);
        var exponent = Base64Url.EncodeToString(parameters.Exponent!);
        var thumbprint = Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{{\"e\":\"{exponent}\",\"kty\":\"RSA\",\"n\":\"{modulus}\"}}")));

        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", alg = "RS256", use = "sig", kid = thumbprint, n = modulus, e = exponent } },
        });
    }

    private static string WithAttribute(string clientJson, string name, string value)
    {
        var client = JsonNode.Parse(clientJson)!;
        client["attributes"] ??= new JsonObject();
        client["attributes"]![name] = value;
        return client.ToJsonString();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private async Task<ClientSnapshot> SnapshotMigrationRunnerAsync()
    {
        using var master = await realm.MasterRealmAdminClientAsync(CancellationToken.None);
        var matches = await RealmProbe.ReadJsonAsync(
            master,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={MigrationRunnerCredential.ClientIdentifier}",
            CancellationToken.None);
        var client = matches.EnumerateArray().Single();

        return new ClientSnapshot(client.GetProperty("id").GetString()!, client.GetRawText());
    }

    private async Task<string?> ReadAuthenticatorTypeAsMasterAsync(ClientSnapshot snapshot)
    {
        using var master = await realm.MasterRealmAdminClientAsync(CancellationToken.None);
        var client = await RealmProbe.ReadJsonAsync(
            master, $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}", CancellationToken.None);

        return client.GetProperty("clientAuthenticatorType").GetString();
    }

    private async Task RestoreMigrationRunnerAsync(ClientSnapshot snapshot)
    {
        using var master = await realm.MasterRealmAdminClientAsync(CancellationToken.None);
        var path = $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}";

        using var existing = await master.GetAsync(path, CancellationToken.None);
        if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            using var created = await master.PostAsync(
                $"admin/realms/{RealmProbe.Realm}/clients", Json(snapshot.Representation), CancellationToken.None);
            created.IsSuccessStatusCode.ShouldBeTrue(
                $"re-creating the deleted migration-runner answered {(int)created.StatusCode}; the realm is now broken for every later test.");
            await RegrantServiceAccountRolesAsync(master, snapshot);
            return;
        }

        using var restored = await master.PutAsync(path, Json(snapshot.Representation), CancellationToken.None);
        restored.IsSuccessStatusCode.ShouldBeTrue(
            $"restoring migration-runner answered {(int)restored.StatusCode}; the realm is left rewritten for every later test.");
    }

    private static async Task RegrantServiceAccountRolesAsync(HttpClient master, ClientSnapshot snapshot)
    {
        var serviceAccount = await RealmProbe.ReadJsonAsync(
            master,
            $"admin/realms/{RealmProbe.Realm}/clients/{snapshot.Identifier}/service-account-user",
            CancellationToken.None);
        var realmManagement = (await RealmProbe.ReadJsonAsync(
            master, $"admin/realms/{RealmProbe.Realm}/clients?clientId=realm-management", CancellationToken.None))
            .EnumerateArray().Single().GetProperty("id").GetString()!;

        List<JsonElement> roles = [];
        foreach (var name in new[] { "query-groups", "view-users", "manage-realm" })
        {
            roles.Add(await RealmProbe.ReadJsonAsync(
                master,
                $"admin/realms/{RealmProbe.Realm}/clients/{realmManagement}/roles/{name}",
                CancellationToken.None));
        }

        using var granted = await master.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/users/{serviceAccount.GetProperty("id").GetString()}/role-mappings/clients/{realmManagement}",
            roles,
            CancellationToken.None);
        granted.IsSuccessStatusCode.ShouldBeTrue(
            $"re-granting migration-runner's roles answered {(int)granted.StatusCode}.");
    }

    private sealed record ClientSnapshot(string Identifier, string Representation);
}
