using System.Net.Http.Json;
using System.Text.Json;
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
        HttpKeycloakAdminClient keycloak = CreateAdminClient();
        string clientId = $"kiosk-privilege-probe-{Guid.CreateVersion7():N}";

        await keycloak.CreateClientAsync(
            KioskRepresentation(clientId), "/fabs/munich", CancellationToken.None);

        try
        {
            IReadOnlyList<string> held = await realm.EffectiveRealmRolesAsync(clientId, CancellationToken.None);

            held.ShouldNotContain(
                RealmProbe.LongLivedCredentialPrivilege,
                "a kiosk is born holding the realm's default privilege, and enrolment must take it back");
        }
        finally
        {
            await DeleteClientAsync(clientId);
        }
    }

    /// <summary>
    /// **The control.** If a freshly created account did not hold the privilege
    /// to begin with, the assertion above would pass against a system that never
    /// removes anything — and the defect would be invisible.
    /// </summary>
    [Fact]
    public async Task An_account_the_provider_creates_holds_it_until_something_removes_it()
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
        string clientId = $"control-probe-{Guid.CreateVersion7():N}";

        // Created directly, bypassing enrolment — so nothing strips it.
        HttpResponseMessage created = await admin.PostAsJsonAsync(
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
            IReadOnlyList<string> held = await realm.EffectiveRealmRolesAsync(clientId, CancellationToken.None);

            held.ShouldContain(
                RealmProbe.LongLivedCredentialPrivilege,
                "if the provider did not grant this by default, the test above would prove nothing");
        }
        finally
        {
            await DeleteClientAsync(clientId);
        }
    }

    /// <summary>
    /// Operators gained nothing — FR-007, checked directly rather than argued
    /// from "we did not touch them". The account is declared in the realm file,
    /// so it receives exactly the roles it names.
    /// </summary>
    [Fact]
    public async Task An_operator_does_not_hold_the_long_lived_credential_privilege()
    {
        IReadOnlyList<string> held = await realm.EffectiveRealmRolesOfUserAsync("operator", CancellationToken.None);

        held.ShouldNotContain(RealmProbe.LongLivedCredentialPrivilege);
    }

    /// <summary>
    /// The other direction, so the assertions above cannot be satisfied by a
    /// realm in which nobody holds the privilege at all — which would make the
    /// feature impossible and every check green.
    /// </summary>
    [Fact]
    public async Task A_wall_display_account_does_hold_it()
    {
        IReadOnlyList<string> held = await realm.EffectiveRealmRolesOfUserAsync("wall-munich", CancellationToken.None);

        held.ShouldContain(
            RealmProbe.LongLivedCredentialPrivilege,
            "a wall display is the one account that may hold it; if none does, nothing can stay up");
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

    private HttpKeycloakAdminClient CreateAdminClient()
    {
        HttpClient http = aspire.CreateKeycloakClient();
        KeycloakAdminOptions options = new()
        {
            BaseUrl = http.BaseAddress!.ToString(),
            Realm = RealmProbe.Realm,
            AdminClientId = RealmProbe.AdminClientId,
            AdminClientSecret = RealmProbe.AdminClientSecret,
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

    private async Task DeleteClientAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);
        foreach (JsonElement client in clients.EnumerateArray())
        {
            await admin.DeleteAsync(
                $"admin/realms/{RealmProbe.Realm}/clients/{client.GetProperty("id").GetString()}",
                CancellationToken.None);
        }
    }
}
