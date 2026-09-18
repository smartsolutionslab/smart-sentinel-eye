using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 180 US1 (#2240). <c>DELETE /kiosks/{clientId}</c> and
/// <c>DELETE /devices/{clientId}</c> take no fab at all today —
/// <see cref="SmartSentinelEye.Identity.Api.KiosksEndpoints"/>'s <c>Disable</c>
/// and its device sibling look the client up by clientId alone
/// (<c>GetByClientIdAsync</c>, unscoped across every fab) and check only its
/// <see cref="SmartSentinelEye.Identity.Domain.RegisteredClient.ClientKind"/>.
/// Any seeded operator can disable another fab's kiosk or device —
/// <c>LegacyManagementBundle</c> makes <c>sse.management</c> satisfy the write
/// policy, and <c>smart-sentinel-eye-web</c> grants it by default, so the
/// attacker principal below is an ordinary seeded operator, not one invented
/// for this test.
///
/// <para>
/// <b>I1/I2 is the whole of the design question this spec answers.</b> A bare
/// <c>fabGuard.EnsureAccessAsync(user, fab)</c> is not enough on its own: if the
/// fab named in the request is the attacker's <i>own</i>, the guard passes
/// honestly while an unscoped lookup still finds and disables the victim's
/// client in a different fab (I2 — the sharper attack, matching spec AS-3). Only
/// a lookup scoped to the named fab in the predicate itself (matching
/// <c>ICameraRepository.GetWithinFabAsync</c>) closes that. I1 is the more
/// obviously wrong case — naming the victim's own fab (AS-2) — and must land on
/// 403; I2 must land on 404, indistinguishable from AS-6's "no such client",
/// so the route cannot be used to enumerate what another fab runs.
/// </para>
///
/// <para>
/// I5 is what turns I1/I2 into more than a status-code check: it reads the
/// victim's client back through Keycloak's own Admin API
/// (<see cref="RealmProbe.AuthorisedAdminClientAsync"/>), not this codebase's
/// <c>registered_clients</c> row — spec 121 (#2165/#2207) is the proof that row
/// and Keycloak can disagree, so only the provider's own answer is trustworthy
/// evidence that a disable never reached it.
/// </para>
///
/// <para>
/// Modelled on
/// <see cref="SmartSentinelEye.Integration.Tests.AuditObservability.CrossFabReadGuardIntegrationTests"/>.
/// Each test enrols/registers its own throwaway client over HTTP — registration
/// mints a real Keycloak client, so rows are never wiped
/// (<see cref="IdempotentRegistrationIntegrationTests"/> records the same
/// reasoning at its head).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabDisableIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    /// <summary>I1 — the attacker names the victim's own fab (AS-2).</summary>
    [Fact]
    public async Task A_dresden_operator_naming_munichs_fab_cannot_disable_a_munich_kiosk()
    {
        using HttpClient munich = await MunichClientAsync();
        string clientId = await EnrollKioskAsync(munich);

        using HttpClient dresden = await DresdenClientAsync();
        HttpResponseMessage attack = await dresden.DeleteAsync($"/kiosks/{clientId}?fabId=munich");

        attack.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"a dresden-only operator naming munich must be refused by the fab guard before any lookup runs; "
            + $"got {(int)attack.StatusCode} {await attack.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(attack)).ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// I2 — the sharper attack: the attacker names their <i>own</i> fab (AS-3).
    /// A guard alone would pass this honestly; only a fab-scoped lookup refuses.
    /// </summary>
    [Fact]
    public async Task A_dresden_operator_naming_their_own_fab_cannot_disable_a_munich_kiosk()
    {
        using HttpClient munich = await MunichClientAsync();
        string clientId = await EnrollKioskAsync(munich);

        using HttpClient dresden = await DresdenClientAsync();
        HttpResponseMessage attack = await dresden.DeleteAsync($"/kiosks/{clientId}?fabId=dresden");

        attack.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            "naming a fab the caller genuinely holds must pass the guard, so only a fab-scoped lookup can "
            + $"refuse; a munich kiosk must be invisible from dresden's own fab. got {(int)attack.StatusCode} "
            + $"{await attack.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(attack)).ShouldBe("KIOSK_NOT_FOUND");
    }

    /// <summary>I3 — the same pair of attacks (I1/I2 shape), against <c>/devices</c>.</summary>
    [Fact]
    public async Task A_dresden_operator_cannot_disable_a_munich_device_naming_either_fab()
    {
        using HttpClient munich = await MunichClientAsync();
        string clientId = await RegisterDeviceAsync(munich);
        using HttpClient dresden = await DresdenClientAsync();

        HttpResponseMessage namingVictim = await dresden.DeleteAsync($"/devices/{clientId}?fabId=munich");
        namingVictim.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"naming the victim's fab must be refused by the guard; got {(int)namingVictim.StatusCode} "
            + $"{await namingVictim.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(namingVictim)).ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");

        HttpResponseMessage namingOwn = await dresden.DeleteAsync($"/devices/{clientId}?fabId=dresden");
        namingOwn.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            $"naming the attacker's own fab must pass the guard and 404 at the fab-scoped lookup instead; "
            + $"got {(int)namingOwn.StatusCode} {await namingOwn.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(namingOwn)).ShouldBe("DEVICE_NOT_FOUND");
    }

    /// <summary>
    /// I4 — the legitimate case, which the fix must not break. Passes both
    /// before and after: today's unscoped lookup happens to also let the
    /// rightful owner through, since it checks kind and nothing else.
    /// </summary>
    [Fact]
    public async Task A_munich_operator_disables_its_own_fabs_kiosk()
    {
        using HttpClient munich = await MunichClientAsync();
        string clientId = await EnrollKioskAsync(munich);

        HttpResponseMessage disabled = await munich.DeleteAsync($"/kiosks/{clientId}?fabId=munich");

        disabled.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"a caller who genuinely holds the target's fab must still be able to disable it; got "
            + $"{(int)disabled.StatusCode} {await disabled.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// I5 — the proof the cross-fab write actually lands, read back through
    /// Keycloak's own Admin API rather than our <c>registered_clients</c> row
    /// (spec 121 is the proof that row can disagree with the provider). Reuses
    /// I1's shape (naming the victim's fab) because that is the attack a bare
    /// guard call would otherwise look like it had already stopped.
    /// </summary>
    [Fact]
    public async Task The_victims_kiosk_stays_enabled_in_keycloak_after_a_cross_fab_disable_attempt()
    {
        using HttpClient munich = await MunichClientAsync();
        string clientId = await EnrollKioskAsync(munich);

        using HttpClient dresden = await DresdenClientAsync();
        await dresden.DeleteAsync($"/kiosks/{clientId}?fabId=munich");

        bool enabled = await IsEnabledInKeycloakAsync(clientId);

        enabled.ShouldBeTrue(
            $"a refused cross-fab request must never reach Keycloak — '{clientId}' must still answer "
            + "enabled:true; a false here means the attack actually disabled the victim's client, "
            + "the availability defect this spec exists to close");
    }

    private Task<HttpClient> MunichClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "admin@munich.test", "Admin1234");

    private Task<HttpClient> DresdenClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-dresden@dresden.test", "Operator1234");

    private static async Task<string> EnrollKioskAsync(HttpClient munich)
    {
        string clientId = $"t180-wall-{Guid.CreateVersion7():N}";

        HttpResponseMessage enrolled = await munich.PostAsJsonAsync(
            "/kiosks/enroll?fabId=munich", new { clientId });
        enrolled.StatusCode.ShouldBe(
            HttpStatusCode.Created, await enrolled.Content.ReadAsStringAsync());

        return (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static async Task<string> RegisterDeviceAsync(HttpClient munich)
    {
        HttpResponseMessage registered = await munich.PostAsJsonAsync(
            "/devices/register?fabId=munich",
            new { deviceType = "plc", deviceIdentifier = $"t180-{Guid.CreateVersion7():N}" });
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());

        return (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;

    private async Task<bool> IsEnabledInKeycloakAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        JsonElement clients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);

        return clients.EnumerateArray().Single().GetProperty("enabled").GetBoolean();
    }
}
