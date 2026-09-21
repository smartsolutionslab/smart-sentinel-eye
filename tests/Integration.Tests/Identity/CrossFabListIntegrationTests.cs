using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 183 US1 (#2281). <c>GET /kiosks</c>, <c>GET /devices</c> and
/// <c>GET /webhook-integrations</c> answer <c>200</c> both before and after
/// this fix — the defect is not a wrong status, it is the wrong <b>rows</b>.
/// Today an omitted <c>fabId</c> becomes <c>Option&lt;FabIdentifier&gt;.None</c>,
/// which <see cref="SmartSentinelEye.Identity.Application.Queries.Handlers.RegisteredClientProjection"/>
/// reads as "no filter" rather than "every fab the caller holds", so every
/// seeded operator can already list every fab's clients — the operator's
/// <c>management-web</c> token names <c>sse.identity.devices.read</c> and
/// <c>sse.identity.kiosks.read</c> explicitly (spec 200, issue #2279), so the
/// listing principals below are ordinary seeded operators, not ones invented
/// for this test.
///
/// <para>
/// <b>I1/I2/I3 are the red, and the assertion is on response-body content,
/// never on the status code.</b> A dresden <c>clientId</c> must not appear in
/// a munich-scoped body with no <c>fabId</c>, and the set of distinct
/// <c>fab</c> values in that body must be exactly <c>{"munich"}</c>. Both are
/// false today, because the projection never filters — that's exactly what
/// must go red before the fix and green after; a status-only assertion here
/// could never fail, which is the class of defect this repo has been bitten
/// by repeatedly (spec 183 tasks.md).
/// </para>
///
/// <para>
/// <b>I4 is the over-narrowing guard, and it must be written even though it
/// passes today.</b> The seeded <c>op-multi@smart-sentinel-eye.test</c> holds
/// both munich and dresden. A fix that resolved a single fab (e.g. "use the
/// first fab in the token") would close the disclosure and still break this
/// operator — and would ship green without this test existing. Written once
/// per endpoint, because the narrowing bug could hide in any one of the three
/// call sites independently.
/// </para>
///
/// <para>
/// I5/I6 are the unchanged-behaviour guard for an explicitly named
/// <c>fabId</c> (spec AS-4) — written once, against <c>/kiosks</c>, since
/// nothing in this change touches the explicit-fab branch and
/// <c>CrossFabDisableIntegrationTests</c> establishes the same
/// not-tripled-across-endpoints precedent for unchanged-behaviour checks.
/// </para>
///
/// <para>
/// Modelled on <c>CrossFabDisableIntegrationTests</c> (spec 180) and reusing
/// its helper shapes. Each test creates its own throwaway client over HTTP —
/// enrolment/registration/rotation each mint a real Keycloak client, so rows
/// are never wiped (<c>IdempotentRegistrationIntegrationTests</c> records the
/// same reasoning at its head). Every created id is prefixed <c>s183-</c> so a
/// failed run is identifiable in the realm.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabListIntegrationTests(AspireFixture aspire)
{
    /// <summary>I1 — the red for <c>/kiosks</c>.</summary>
    [Fact]
    public async Task A_munich_operator_listing_kiosks_without_a_fabId_does_not_see_a_dresden_kiosk()
    {
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await EnrollKioskAsync(dresden, "dresden");

        using HttpClient munich = await MunichClientAsync();
        // Enrolled explicitly rather than relying on some other test's side
        // effect to have left a munich row behind: the second assertion below
        // needs at least one munich row to exist by construction, not by luck
        // of xUnit's unordered execution within the collection.
        string munichClientId = await EnrollKioskAsync(munich, "munich");

        JsonElement rows = await ListAsync(munich, "/kiosks");

        ClientIds(rows).ShouldNotContain(
            dresdenClientId,
            "a dresden client must never appear in a munich-scoped list with no fabId — "
            + "this is the disclosure #2281 exists to close");
        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller's own fab must still be listed — this is the non-empty control that makes the "
            + "distinct-fabs assertion below mean something, rather than pass on an empty result");
        DistinctFabs(rows).ShouldBe(
            ["munich"],
            "an omitted fabId must narrow the list to the caller's own fabs, not return every fab's rows");
    }

    /// <summary>I2 — the same shape for <c>/devices</c>.</summary>
    [Fact]
    public async Task A_munich_operator_listing_devices_without_a_fabId_does_not_see_a_dresden_device()
    {
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await RegisterDeviceAsync(dresden, "dresden");

        using HttpClient munich = await MunichClientAsync();
        // Enrolled explicitly, same reasoning as I1: the distinct-fabs
        // assertion below needs a guaranteed munich row, not one that happens
        // to be left behind by another test's unordered execution.
        string munichClientId = await RegisterDeviceAsync(munich, "munich");

        JsonElement rows = await ListAsync(munich, "/devices");

        ClientIds(rows).ShouldNotContain(
            dresdenClientId,
            "a dresden device must never appear in a munich-scoped list with no fabId — "
            + "this is the disclosure #2281 exists to close");
        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller's own fab must still be listed — this is the non-empty control that makes the "
            + "distinct-fabs assertion below mean something, rather than pass on an empty result");
        DistinctFabs(rows).ShouldBe(
            ["munich"],
            "an omitted fabId must narrow the list to the caller's own fabs, not return every fab's rows");
    }

    /// <summary>I3 — the same shape for <c>/webhook-integrations</c>.</summary>
    [Fact]
    public async Task A_munich_operator_listing_webhook_integrations_without_a_fabId_does_not_see_a_dresden_integration()
    {
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await CreateWebhookIntegrationAsync(dresden, "dresden");

        using HttpClient munich = await MunichClientAsync();
        // Created explicitly, same reasoning as I1/I2: the distinct-fabs
        // assertion below needs a guaranteed munich row, not one that happens
        // to be left behind by another test's unordered execution.
        string munichClientId = await CreateWebhookIntegrationAsync(munich, "munich");

        JsonElement rows = await ListAsync(munich, "/webhook-integrations");

        ClientIds(rows).ShouldNotContain(
            dresdenClientId,
            "a dresden webhook integration must never appear in a munich-scoped list with no fabId — "
            + "this is the disclosure #2281 exists to close");
        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller's own fab must still be listed — this is the non-empty control that makes the "
            + "distinct-fabs assertion below mean something, rather than pass on an empty result");
        DistinctFabs(rows).ShouldBe(
            ["munich"],
            "an omitted fabId must narrow the list to the caller's own fabs, not return every fab's rows");
    }

    /// <summary>
    /// I4 for <c>/kiosks</c> — the over-narrowing guard. Passes today
    /// (vacuously, because nothing is filtered), and must keep passing once
    /// the fix lands, for the right reason: a fab <b>set</b>, not a single
    /// inferred fab.
    /// </summary>
    [Fact]
    public async Task A_multi_fab_operator_listing_kiosks_without_a_fabId_sees_both_their_fabs_and_no_others()
    {
        using HttpClient munich = await MunichClientAsync();
        string munichClientId = await EnrollKioskAsync(munich, "munich");
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await EnrollKioskAsync(dresden, "dresden");
        using HttpClient berlin = await BerlinClientAsync();
        string berlinClientId = await EnrollKioskAsync(berlin, "berlin");

        using HttpClient multi = await MultiClientAsync();
        JsonElement rows = await ListAsync(multi, "/kiosks");

        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller holding munich and dresden must still see their munich kiosks — "
            + "a fix that narrowed to one fab would drop these");
        ClientIds(rows).ShouldContain(
            dresdenClientId,
            "a caller holding munich and dresden must still see their dresden kiosks — "
            + "a fix that resolved only 'the first fab in the token' would drop these silently");
        ClientIds(rows).ShouldNotContain(
            berlinClientId,
            "a fab the caller does not hold must stay excluded even for a multi-fab caller");
    }

    /// <summary>I4 for <c>/devices</c>.</summary>
    [Fact]
    public async Task A_multi_fab_operator_listing_devices_without_a_fabId_sees_both_their_fabs_and_no_others()
    {
        using HttpClient munich = await MunichClientAsync();
        string munichClientId = await RegisterDeviceAsync(munich, "munich");
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await RegisterDeviceAsync(dresden, "dresden");
        using HttpClient berlin = await BerlinClientAsync();
        string berlinClientId = await RegisterDeviceAsync(berlin, "berlin");

        using HttpClient multi = await MultiClientAsync();
        JsonElement rows = await ListAsync(multi, "/devices");

        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller holding munich and dresden must still see their munich devices — "
            + "a fix that narrowed to one fab would drop these");
        ClientIds(rows).ShouldContain(
            dresdenClientId,
            "a caller holding munich and dresden must still see their dresden devices — "
            + "a fix that resolved only 'the first fab in the token' would drop these silently");
        ClientIds(rows).ShouldNotContain(
            berlinClientId,
            "a fab the caller does not hold must stay excluded even for a multi-fab caller");
    }

    /// <summary>I4 for <c>/webhook-integrations</c>.</summary>
    [Fact]
    public async Task A_multi_fab_operator_listing_webhook_integrations_without_a_fabId_sees_both_their_fabs_and_no_others()
    {
        using HttpClient munich = await MunichClientAsync();
        string munichClientId = await CreateWebhookIntegrationAsync(munich, "munich");
        using HttpClient dresden = await DresdenClientAsync();
        string dresdenClientId = await CreateWebhookIntegrationAsync(dresden, "dresden");
        using HttpClient berlin = await BerlinClientAsync();
        string berlinClientId = await CreateWebhookIntegrationAsync(berlin, "berlin");

        using HttpClient multi = await MultiClientAsync();
        JsonElement rows = await ListAsync(multi, "/webhook-integrations");

        ClientIds(rows).ShouldContain(
            munichClientId,
            "a caller holding munich and dresden must still see their munich webhook integrations — "
            + "a fix that narrowed to one fab would drop these");
        ClientIds(rows).ShouldContain(
            dresdenClientId,
            "a caller holding munich and dresden must still see their dresden webhook integrations — "
            + "a fix that resolved only 'the first fab in the token' would drop these silently");
        ClientIds(rows).ShouldNotContain(
            berlinClientId,
            "a fab the caller does not hold must stay excluded even for a multi-fab caller");
    }

    /// <summary>
    /// I5 — naming a fab the caller does not hold is unchanged behaviour
    /// (spec AS-4). Written once, against <c>/kiosks</c>: nothing in this fix
    /// touches the explicit-fab branch, so a defect here would not be
    /// endpoint-specific.
    /// </summary>
    [Fact]
    public async Task A_munich_operator_naming_dresden_is_still_refused()
    {
        using HttpClient munich = await MunichClientAsync();

        HttpResponseMessage response = await munich.GetAsync("/kiosks?fabId=dresden");

        response.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"naming a fab the caller does not hold must still be refused by the fab guard, unchanged by this "
            + $"fix; got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        (await ProblemTitleAsync(response)).ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// I6 — naming a fab the caller does hold still returns only that fab's
    /// rows (spec AS-4). Written once, against <c>/kiosks</c>, for the same
    /// reason as I5.
    /// </summary>
    [Fact]
    public async Task A_munich_operator_naming_munich_still_sees_only_munich_rows()
    {
        using HttpClient munich = await MunichClientAsync();
        string munichClientId = await EnrollKioskAsync(munich, "munich");
        using HttpClient dresden = await DresdenClientAsync();
        await EnrollKioskAsync(dresden, "dresden");

        JsonElement rows = await ListAsync(munich, "/kiosks?fabId=munich");

        ClientIds(rows).ShouldContain(
            munichClientId,
            "naming the caller's own fab must still return that fab's rows, unchanged by this fix");
        DistinctFabs(rows).ShouldBe(
            ["munich"],
            "naming a single fab must still narrow the list to exactly that fab, unchanged by this fix");
    }

    private Task<HttpClient> MunichClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "admin@munich.test", "Admin1234");

    private Task<HttpClient> DresdenClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-dresden@dresden.test", "Operator1234");

    private Task<HttpClient> BerlinClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-berlin@berlin.test", "Operator1234");

    private Task<HttpClient> MultiClientAsync() =>
        aspire.CreateAuthenticatedClientAsync("identity", "op-multi@smart-sentinel-eye.test", "Operator1234");

    private static async Task<string> EnrollKioskAsync(HttpClient client, string fab)
    {
        string clientId = $"s183-wall-{Guid.CreateVersion7():N}";

        HttpResponseMessage enrolled = await client.PostAsJsonAsync(
            $"/kiosks/enroll?fabId={fab}", new { clientId });
        enrolled.StatusCode.ShouldBe(
            HttpStatusCode.Created, await enrolled.Content.ReadAsStringAsync());

        return (await enrolled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static async Task<string> RegisterDeviceAsync(HttpClient client, string fab)
    {
        HttpResponseMessage registered = await client.PostAsJsonAsync(
            $"/devices/register?fabId={fab}",
            new { deviceType = "plc", deviceIdentifier = $"s183-{Guid.CreateVersion7():N}" });
        registered.StatusCode.ShouldBe(
            HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());

        return (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static async Task<string> CreateWebhookIntegrationAsync(HttpClient client, string fab)
    {
        string name = $"s183-{Guid.CreateVersion7():N}";
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        HttpResponseMessage created = await client.SendAsync(request);
        created.StatusCode.ShouldBe(
            HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetString()!;
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string path)
    {
        HttpResponseMessage listed = await client.GetAsync(path);
        listed.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"a scoped list call must always answer 200, even with zero rows; got {(int)listed.StatusCode} "
            + $"{await listed.Content.ReadAsStringAsync()}");

        return await listed.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string> ClientIds(JsonElement rows) =>
        rows.EnumerateArray().Select(row => row.GetProperty("clientId").GetString()!);

    private static string[] DistinctFabs(JsonElement rows) =>
        [.. rows.EnumerateArray().Select(row => row.GetProperty("fab").GetString()!).Distinct()];

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()!;
}
