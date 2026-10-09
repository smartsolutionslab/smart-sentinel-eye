using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 304 US1 (#2626). The issue's premise was that <c>POST
/// /devices/register</c>'s 409 leaked which fab held a conflicting
/// <c>clientId</c>; re-reading the handler found the opposite — both
/// conflict branches (<c>RegisterDeviceCommandHandler.cs:46-51</c>, the
/// realm-global DB lookup, and <c>:79-82</c>, Keycloak's own conflict) already
/// return the identical holder-neutral
/// <c>RegisterDeviceError.DeviceAlreadyRegistered(clientId)</c> — the
/// <b>caller's own submitted</b> clientId, never the holder's fab. Nothing
/// here changes production code; these tests pin the already-correct
/// behaviour so a future change cannot silently regress it (ADR-0139,
/// characterisation — observed green).
///
/// <para>
/// Mirrors <see cref="CrossFabDisableIntegrationTests"/> (spec 180 US1), the
/// precedent this issue names: DELETE closes its one-bit disclosure with a
/// fab-scoped lookup that answers 404 — the same indistinguishable-from-a-
/// non-conflict shape. Register cannot do the same (a 201 would be a lie for
/// an id it cannot create), so its nearest equivalent is "the same 409
/// whoever holds it" — already true, and what C1 pins.
/// </para>
///
/// <para>
/// <b>What remains disclosed, and is deliberately not tested here</b>: a
/// cross-fab caller still learns the one-bit fact that <i>some</i> client in
/// the realm holds the id (409 vs 201) — closing that needs a fab-scoped
/// clientId namespace, an ADR-sized change, not this slice (spec 304 §6).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabRegistrationConflictIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// I1/I3 — the cross-fab conflict's body must be byte-for-byte the
    /// same-fab conflict's body, property set included, except
    /// <c>traceId</c> (stamped per request by <c>AddProblemDetails</c>,
    /// legitimately different across two separate requests). The dresden
    /// body must also never contain "munich" anywhere.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_registration_conflict_answers_exactly_what_a_same_fab_conflict_answers()
    {
        using HttpClient munich = await MunichClientAsync();
        string device = NewDeviceIdentifier();
        await RegisterAsync(munich, device, "munich", HttpStatusCode.Created);

        HttpResponseMessage sameFabConflict = await SendRegisterAsync(munich, device, "munich");
        using HttpClient dresden = await DresdenClientAsync();
        HttpResponseMessage crossFabConflict = await SendRegisterAsync(dresden, device, "dresden");

        sameFabConflict.StatusCode.ShouldBe(
            HttpStatusCode.Conflict, await aspire.DiagnoseAsync("identity", sameFabConflict));
        crossFabConflict.StatusCode.ShouldBe(
            HttpStatusCode.Conflict, await aspire.DiagnoseAsync("identity", crossFabConflict));

        string sameFabBody = await sameFabConflict.Content.ReadAsStringAsync();
        string crossFabBody = await crossFabConflict.Content.ReadAsStringAsync();

        JsonElement sameFabJson = JsonDocument.Parse(sameFabBody).RootElement;
        JsonElement crossFabJson = JsonDocument.Parse(crossFabBody).RootElement;

        PropertyNamesExceptTraceId(crossFabJson).ShouldBe(
            PropertyNamesExceptTraceId(sameFabJson),
            "a cross-fab conflict must carry exactly the same body shape as a same-fab conflict, "
            + $"traceId excepted; same-fab: {sameFabBody}; cross-fab: {crossFabBody}");

        foreach (string property in new[] { "type", "title", "status", "detail" })
        {
            crossFabJson.GetProperty(property).ToString().ShouldBe(
                sameFabJson.GetProperty(property).ToString(),
                $"'{property}' must be identical between a same-fab and a cross-fab conflict; "
                + $"same-fab: {sameFabBody}; cross-fab: {crossFabBody}");
        }

        crossFabBody.ShouldNotContain(
            "munich",
            customMessage: "the cross-fab conflict's body must never name the holder's fab — "
            + $"got: {crossFabBody}");
    }

    /// <summary>
    /// I2 — the uniqueness check still refuses across fabs, and the refusal
    /// creates nothing: dresden's own device list must not show the id, and
    /// Keycloak must hold exactly one client for it, still attributed to
    /// munich.
    /// </summary>
    [Fact]
    public async Task A_cross_fab_registration_conflict_creates_nothing_in_either_fab()
    {
        using HttpClient munich = await MunichClientAsync();
        string device = NewDeviceIdentifier();
        string clientId = await RegisterAsync(munich, device, "munich", HttpStatusCode.Created);

        using HttpClient dresden = await DresdenClientAsync();
        HttpResponseMessage dresdenConflict = await SendRegisterAsync(dresden, device, "dresden");
        dresdenConflict.StatusCode.ShouldBe(
            HttpStatusCode.Conflict,
            "the cross-fab attempt must actually hit the uniqueness conflict, not some unrelated "
            + "rejection (e.g. a 403) that would make the side-effect checks below pass vacuously; "
            + await aspire.DiagnoseAsync("identity", dresdenConflict));

        HttpResponseMessage dresdenList = await dresden.GetAsync("/devices?fabId=dresden");
        dresdenList.StatusCode.ShouldBe(
            HttpStatusCode.OK, await aspire.DiagnoseAsync("identity", dresdenList));
        JsonElement dresdenRows = await dresdenList.Content.ReadFromJsonAsync<JsonElement>();
        ClientIds(dresdenRows).ShouldNotContain(
            clientId,
            "a cross-fab conflict attempt must never create a row dresden can see — "
            + "the uniqueness check must refuse before anything is persisted");

        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);
        JsonElement keycloakClients = await RealmProbe.ReadJsonAsync(
            admin,
            $"admin/realms/{RealmProbe.Realm}/clients?clientId={Uri.EscapeDataString(clientId)}",
            CancellationToken.None);
        JsonElement onlyClient = keycloakClients.EnumerateArray().ShouldHaveSingleItem(
            $"exactly one Keycloak client must exist for '{clientId}' — a cross-fab conflict attempt "
            + "must never create a second one under dresden");
        onlyClient.GetProperty("attributes").GetProperty("sse.fab").GetString().ShouldBe(
            "munich",
            "the sole Keycloak client for this id must stay attributed to munich, the fab that actually "
            + "holds it");
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

    private static string[] PropertyNamesExceptTraceId(JsonElement body) =>
        [.. body.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => name != "traceId")
            .OrderBy(name => name, StringComparer.Ordinal)];
}
