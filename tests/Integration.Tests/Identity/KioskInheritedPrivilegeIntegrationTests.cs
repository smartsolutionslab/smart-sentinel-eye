using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 052 US1 — who may mint a credential that never expires, asked of the
/// <b>running provider</b>.
///
/// <para>
/// <b>This is the only check that can answer the question, and the reason is
/// the whole feature.</b> The realm hands every account created after import a
/// default privilege that includes <c>offline_access</c>. Accounts declared in
/// the realm file are unaffected — they receive exactly the roles they name — so
/// a test that reads the file sees four wall displays holding it and concludes
/// the widening is contained. Meanwhile every kiosk the system enrols is born
/// holding it too, and the file says nothing about them.
/// </para>
///
/// <para>
/// The previous attempt shipped exactly that mistake: its architecture guard
/// read the realm file, stayed green for the entire feature, and the claim it
/// stood for was false throughout. <b>So nothing here reads the file.</b>
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class KioskInheritedPrivilegeIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    [Fact]
    public async Task A_kiosk_enrolled_at_runtime_does_not_hold_the_long_lived_credential_privilege()
    {
        var keycloak = CreateAdminClient();
        var clientId = $"kiosk-privilege-probe-{Guid.CreateVersion7():N}";

        await keycloak.CreateClientAsync(
            KioskRepresentation(clientId), "/fabs/munich", CancellationToken.None);

        try
        {
            var held = await realm.EffectiveRealmRolesAsync(clientId, CancellationToken.None);

            held.ShouldNotContain(
                RealmProbe.LongLivedCredentialPrivilege,
                "a kiosk is born holding the realm's default privilege, and enrolment must take it back");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Spec 440 (ADR-0181): INVERTED from "holds it until something removes it".
    /// Narrowing the realm's default composite after import means an account the
    /// provider creates does not inherit <c>offline_access</c>. Its anti-vacuity
    /// duty (that the privilege exists to be held) now belongs to
    /// <see cref="A_wall_display_account_does_hold_it"/>, widened to all four walls.
    /// It still holds <c>user</c> and <c>uma_authorization</c>: the narrowing took
    /// one member out of the composite, not the composite away.
    /// </summary>
    [Fact]
    public async Task An_account_the_provider_creates_does_not_hold_it()
    {
        using var admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
        var clientId = $"control-probe-{Guid.CreateVersion7():N}";

        // Created directly, bypassing enrolment — so nothing strips it.
        var created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId,
                enabled = true,
                publicClient = false,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
            },
            CancellationToken.None);
        created.IsSuccessStatusCode.ShouldBeTrue();

        try
        {
            var held = await realm.EffectiveRealmRolesAsync(clientId, CancellationToken.None);

            held.ShouldNotContain(
                RealmProbe.LongLivedCredentialPrivilege,
                "the realm's default composite is narrowed at boot, so nothing inherits the privilege");
            held.ShouldContain("user");
            held.ShouldContain("uma_authorization");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>The issue's literal case: a person made by hand in the console.</summary>
    [Fact]
    public async Task A_person_account_created_through_the_admin_api_does_not_hold_it()
    {
        var username = $"hand-made-{Guid.CreateVersion7():N}";
        var userId = await realm.CreateUserAsync(username, CancellationToken.None);

        try
        {
            var held = await realm.EffectiveRealmRolesOfUserAsync(username, CancellationToken.None);

            held.ShouldNotContain(RealmProbe.LongLivedCredentialPrivilege);
            held.ShouldContain("user");
        }
        finally
        {
            await realm.DeleteUserAsync(userId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Operators gained nothing and lost nothing — checked directly rather than
    /// argued from "we did not touch them". Declared in the realm file, so they
    /// receive exactly the roles they name.
    /// </summary>
    [Fact]
    public async Task An_operator_holds_exactly_the_user_role()
    {
        var held = await realm.EffectiveRealmRolesOfUserAsync("operator", CancellationToken.None);

        held.ShouldBe(["user"]);
    }

    /// <summary>
    /// The other direction, so the assertions above cannot be satisfied by a
    /// realm in which nobody holds the privilege at all. Widened from one wall to
    /// all four (ADR-0181 §5): this is where the inverted control's duty went.
    /// </summary>
    [Theory]
    [InlineData("wall-munich")]
    [InlineData("wall-dresden")]
    [InlineData("wall-berlin")]
    [InlineData("wall-hamburg")]
    public async Task A_wall_display_account_does_hold_it(string wallAccount)
    {
        var held = await realm.EffectiveRealmRolesOfUserAsync(wallAccount, CancellationToken.None);

        held.ShouldContain(
            RealmProbe.LongLivedCredentialPrivilege,
            "a wall display is the one account that may hold it; if none does, nothing can stay up");
    }

    /// <summary>
    /// The composite that was narrowed is the one in force. A realm file that
    /// declared <c>default-roles-*</c> makes Keycloak create a <c>-1</c> sibling
    /// carrying <c>offline_access</c> and bind the realm to that: a silent decoy
    /// (spec 080 §2.3). So this reads the running realm, not the file.
    /// </summary>
    [Fact]
    public async Task The_default_role_in_force_is_the_narrowed_composite_and_no_decoy_exists()
    {
        using var admin = await realm.MasterRealmAdminClientAsync(CancellationToken.None);
        var expected = $"default-roles-{RealmProbe.Realm}";

        var representation = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}", CancellationToken.None);
        var roles = await RealmProbe.ReadJsonAsync(
            admin, $"admin/realms/{RealmProbe.Realm}/roles?max=1000", CancellationToken.None);
        var members = await realm.DefaultCompositeMembersAsync(CancellationToken.None);

        representation.GetProperty("defaultRole").GetProperty("name").GetString().ShouldBe(expected);
        roles.EnumerateArray()
            .Select(role => role.GetProperty("name").GetString())
            .ShouldNotContain($"{expected}-1");
        members.ShouldNotContain(RealmProbe.LongLivedCredentialPrivilege);
    }

    /// <summary>Boot-every-time contract: the narrowing repeated as migration-runner succeeds and changes nothing.</summary>
    [Fact]
    public async Task Narrowing_again_as_migration_runner_succeeds_and_leaves_the_composite_unchanged()
    {
        var before = await realm.DefaultCompositeMembersAsync(CancellationToken.None);
        var migrationRunner = CreateAdminClient(
            MigrationRunnerCredential.ClientIdentifier,
            adminClientSecret: string.Empty,
            adminClientPrivateKey: await MigrationRunnerCredential.PrivateKeyAsync());

        await migrationRunner.RemoveRealmDefaultPrivilegeAsync(
            RealmProbe.LongLivedCredentialPrivilege, CancellationToken.None);

        var after = await realm.DefaultCompositeMembersAsync(CancellationToken.None);
        after.ShouldBe(before);
    }

    /// <summary>
    /// Characterisation (green before and after): the authority is confined.
    /// identity-admin never held <c>manage-realm</c> and must not gain it. Sent as
    /// raw HTTP so the verdict is Keycloak's, not the adapter's.
    /// </summary>
    [Fact]
    public async Task Identity_admin_cannot_change_the_default_composite()
    {
        var before = await realm.DefaultCompositeMembersAsync(CancellationToken.None);
        using var master = await realm.MasterRealmAdminClientAsync(CancellationToken.None);
        var role = await RealmProbe.ReadJsonAsync(
            master,
            $"admin/realms/{RealmProbe.Realm}/roles/{RealmProbe.LongLivedCredentialPrivilege}",
            CancellationToken.None);
        using var identityAdmin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        using HttpRequestMessage request = new(
            HttpMethod.Delete,
            $"admin/realms/{RealmProbe.Realm}/roles/default-roles-{RealmProbe.Realm}/composites")
        {
            Content = JsonContent.Create(new[] { role }),
        };
        using var response = await identityAdmin.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await realm.DefaultCompositeMembersAsync(CancellationToken.None)).ShouldBe(before);
    }

    private static KeycloakClientRepresentation KioskRepresentation(string clientId) =>
        new(
            ClientId: clientId,
            Name: $"Kiosk {clientId}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes: KeycloakScopeBundles.Kiosk,
            OptionalClientScopes: [],
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "kiosk",
                ["sse.fab"] = "munich",
            });

    private HttpKeycloakAdminClient CreateAdminClient(
        string adminClientId = RealmProbe.AdminClientId,
        string adminClientSecret = RealmProbe.AdminClientSecret,
        string adminClientPrivateKey = "")
    {
        var http = aspire.CreateKeycloakClient();
        KeycloakAdminOptions options = new()
        {
            BaseUrl = http.BaseAddress!.ToString(),
            Realm = RealmProbe.Realm,
            AdminClientId = adminClientId,
            AdminClientSecret = adminClientSecret,
            AdminClientPrivateKey = adminClientPrivateKey,
        };
        KeycloakAdminTokenProvider tokens = new(
            new FakeHttpClientFactory(aspire.CreateKeycloakClient()),
            Options.Create(options),
            TimeProvider.System,
            NullLogger<KeycloakAdminTokenProvider>.Instance);

        // The composition the DI registration performs, spelled out: the client
        // no longer authorises itself, so the token provider reaches it through
        // the same delegating handler AddHttpMessageHandler would insert.
        HttpClient authorised = new(
            new KeycloakAdminAuthorizationHandler(tokens)
            {
                InnerHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                },
            })
        {
            BaseAddress = http.BaseAddress,
        };

        return new HttpKeycloakAdminClient(
            authorised, Options.Create(options), NullLogger<HttpKeycloakAdminClient>.Instance);
    }
}
