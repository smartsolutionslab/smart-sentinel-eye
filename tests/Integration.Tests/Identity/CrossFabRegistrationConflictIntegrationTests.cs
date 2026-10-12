using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 304 US1 (#2626) characterised the one-bit disclosure left open by its
/// own slice: a cross-fab caller using the same device identifier still hit
/// the realm-global uniqueness conflict (409 vs 201), revealing that
/// <i>some</i> fab already held it. Spec 304 §6 named closing that gap an
/// ADR-sized change, not its own slice.
///
/// <para>
/// ADR-0172 is that change. Device clientIds are now fab-qualified
/// (<c>plc.{fab}.{identifier}</c>, <c>.</c>-separated so the composition
/// stays injective even when a fab identifier or device identifier itself
/// contains a <c>-</c>), so the two fabs' attempts compose genuinely
/// different strings and are never the same key to begin with — the fix
/// is the composition itself, not the lookup. <c>RegisterDeviceCommandHandler</c>
/// also moved its existence check to the fab-scoped <c>GetWithinFabAsync</c>
/// as defense in depth, but that alone would not have closed this: Keycloak's
/// own clientId uniqueness constraint is realm-wide regardless of what the
/// DB lookup checks. There is no longer a cross-fab conflict to characterise:
/// the disclosure is closed structurally, not by matching the same-fab error
/// body (what this file tested before ADR-0172).
/// </para>
///
/// <para>
/// Mirrors <see cref="CrossFabDisableIntegrationTests"/> (spec 180 US1) in
/// spirit — both close spec 304's family of one-bit leaks — but by a
/// different mechanism: that slice fab-scopes the <i>lookup</i> for DELETE;
/// this one fab-qualifies the <i>clientId string itself</i>, so the fix
/// reaches every caller of the existence check, not just one endpoint.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabRegistrationConflictIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// Two fabs registering the same device identifier now both succeed,
    /// each with its own fab-qualified clientId — proof the uniqueness
    /// check no longer spans fabs.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_registration_with_the_same_device_identifier_now_succeeds_in_both_fabs()
    {
        using HttpClient munich = await MunichClientAsync();
        string device = NewDeviceIdentifier();
        string munichClientId = await RegisterAsync(munich, device, "munich", HttpStatusCode.Created);

        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await RegisterAsync(dresden, device, "dresden", HttpStatusCode.Created);

        dresdenClientId.ShouldNotBe(
            munichClientId,
            "fab-qualification must make the two fabs' clientIds distinct strings, "
            + $"even for the same device identifier; munich: {munichClientId}; dresden: {dresdenClientId}");
        munichClientId.ShouldBe($"plc.munich.{device}");
        dresdenClientId.ShouldBe($"plc.dresden.{device}");
    }

    /// <summary>
    /// Each fab's registration creates its own client, attributed to its own
    /// fab, and neither disturbs the other's: dresden's registration must
    /// not alter munich's row, and Keycloak must hold two separate clients.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_registration_creates_its_own_client_without_disturbing_the_other_fab()
    {
        using HttpClient munich = await MunichClientAsync();
        string device = NewDeviceIdentifier();
        string munichClientId = await RegisterAsync(munich, device, "munich", HttpStatusCode.Created);

        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await RegisterAsync(dresden, device, "dresden", HttpStatusCode.Created);

        HttpResponseMessage dresdenList = await dresden.GetAsync("/devices?fabId=dresden");
        dresdenList.StatusCode.ShouldBe(
            HttpStatusCode.OK, await aspire.DiagnoseAsync("identity", dresdenList));
        JsonElement dresdenRows = await dresdenList.Content.ReadFromJsonAsync<JsonElement>();
        ClientIds(dresdenRows).ShouldContain(
            dresdenClientId,
            "dresden's own registration must appear in its own device list");

        HttpResponseMessage munichList = await munich.GetAsync("/devices?fabId=munich");
        munichList.StatusCode.ShouldBe(
            HttpStatusCode.OK, await aspire.DiagnoseAsync("identity", munichList));
        JsonElement munichRows = await munichList.Content.ReadFromJsonAsync<JsonElement>();
        ClientIds(munichRows).ShouldContain(
            munichClientId,
            "munich's original registration must still exist, untouched by dresden's later one");

        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        JsonElement munichKeycloakClients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(munichClientId)}",
            CancellationToken.None);
        JsonElement munichKeycloakClient = munichKeycloakClients.EnumerateArray().ShouldHaveSingleItem(
            $"exactly one Keycloak client must exist for '{munichClientId}'");
        munichKeycloakClient.GetProperty("attributes").GetProperty("sse.fab").GetString().ShouldBe("munich");

        JsonElement dresdenKeycloakClients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(dresdenClientId)}",
            CancellationToken.None);
        JsonElement dresdenKeycloakClient = dresdenKeycloakClients.EnumerateArray().ShouldHaveSingleItem(
            $"exactly one Keycloak client must exist for '{dresdenClientId}'");
        dresdenKeycloakClient.GetProperty("attributes").GetProperty("sse.fab").GetString().ShouldBe("dresden");
    }

    private Task<HttpClient> MunichClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "admin@munich.test", SeededCredentials.AdminMunich);

    private Task<HttpClient> DresdenClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-dresden@dresden.test", SeededCredentials.OpDresden);

    private static string NewDeviceIdentifier() => $"t304-{Guid.CreateVersion7():N}";

    private static async Task<string> RegisterAsync(
        HttpClient client, string device, string fab, HttpStatusCode expected)
    {
        HttpResponseMessage response = await SendRegisterAsync(client, device, fab);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static Task<HttpResponseMessage> SendRegisterAsync(HttpClient client, string device, string fab) =>
        client.PostAsJsonAsync(
            $"/devices/register?fabId={fab}",
            new { deviceType = "plc", deviceIdentifier = device });

    private static IEnumerable<string> ClientIds(JsonElement rows) =>
        rows.EnumerateArray().Select(row => row.GetProperty("clientId").GetString()!);
}
