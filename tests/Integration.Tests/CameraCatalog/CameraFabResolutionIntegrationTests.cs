using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 015 T022 — the fab decision table for the cameras API, over real HTTP.
/// Covers SC-002 and SC-004.
///
/// <para>
/// <c>FabResolutionTests</c> drives the same table with synthetic principals,
/// but nothing else proves a real Keycloak token reaches these endpoints with
/// the groups claim intact. Mirrors <c>RuleFabResolutionIntegrationTests</c>
/// and <c>VariableFabResolutionIntegrationTests</c>, which exist for the same
/// reason on their own APIs.
/// </para>
///
/// <para>
/// Narrower than the sibling suites, and not by choice: <c>CameraCatalog</c>
/// exposes only <c>POST /cameras</c> and <c>GET /cameras</c>. There is no
/// read-by-name, so the "another fab's camera is indistinguishable from one
/// that never existed" row and the ambiguity row have no endpoint to drive —
/// FR-006 and FR-010 were withdrawn for that reason, and the endpoints are
/// tracked as #1435.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CameraFabResolutionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_multi_fab_operator_registering_without_naming_a_fab_is_refused()
    {
        // Not inferred, and not tie-broken: either would file the camera under
        // a fab the operator never chose (ADR-0114).
        using HttpClient cameras = await ClientFor(MultiFabOperator);

        HttpResponseMessage refused = await cameras.PostAsJsonAsync("/cameras", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await BodyAsync(refused));
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("CAMERA_FAB_REQUIRED");
    }

    [Fact]
    public async Task A_multi_fab_operator_naming_one_of_their_fabs_is_accepted()
    {
        using HttpClient cameras = await ClientFor(MultiFabOperator);
        string name = UniqueName();

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras?fabId=dresden", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        (await FabOfAsync(cameras, name)).ShouldBe("dresden");
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        // The dresden-only operator against munich: 403, not 404. Nothing is
        // hidden — the caller named a fab, and the answer is about the fab.
        using HttpClient cameras = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await cameras.PostAsJsonAsync("/cameras?fabId=munich", Body(UniqueName()));

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    [Fact]
    public async Task A_single_fab_operator_has_dresden_inferred_not_the_default()
    {
        using HttpClient cameras = await ClientFor(DresdenOperator);
        string name = UniqueName();

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", Body(name));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await BodyAsync(created));

        // dresden, not munich. Everything else in the system defaults to
        // munich, so a broken inference that fell back to the default would
        // pass against a munich operator and only fail here.
        (await FabOfAsync(cameras, name)).ShouldBe("dresden");
    }

    [Fact]
    public async Task The_listing_spans_every_fab_the_caller_holds()
    {
        // FR-005, and the asymmetry with the write path: a read does not have
        // to choose.
        using HttpClient cameras = await ClientFor(MultiFabOperator);
        string inMunich = UniqueName();
        string inDresden = UniqueName();

        (await cameras.PostAsJsonAsync("/cameras?fabId=munich", Body(inMunich)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await cameras.PostAsJsonAsync("/cameras?fabId=dresden", Body(inDresden)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        string[] names = await NamesAsync(cameras);

        names.ShouldContain(inMunich);
        names.ShouldContain(inDresden);
    }

    [Fact]
    public async Task The_listing_omits_a_fab_the_caller_does_not_hold()
    {
        using HttpClient owner = await ClientFor(MultiFabOperator);
        string inMunich = UniqueName();
        (await owner.PostAsJsonAsync("/cameras?fabId=munich", Body(inMunich)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient outsider = await ClientFor(DresdenOperator);

        (await NamesAsync(outsider)).ShouldNotContain(inMunich);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static object Body(string name) => new
    {
        name,
        rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
    };

    private static async Task<string[]> NamesAsync(HttpClient cameras)
    {
        HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!)];
    }

    /// <summary>
    /// Read back through the listing, because there is no read-by-name
    /// endpoint (#1435). The fab on the row is what FR-013 puts there.
    /// </summary>
    private static async Task<string> FabOfAsync(HttpClient cameras, string name)
    {
        HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return page.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("name").GetString() == name)
            .GetProperty("fab").GetString()!;
    }

    private static string UniqueName() => $"Cam-{Guid.NewGuid():N}"[..12];

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"camera-catalog log:{Environment.NewLine}{aspire.RecentLogs("camera-catalog")}";
}
