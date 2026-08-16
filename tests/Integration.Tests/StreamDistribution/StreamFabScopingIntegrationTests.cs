using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 016 T019 — SC-001 and SC-002, over real HTTP with real Keycloak tokens.
///
/// <para>
/// The handler tests prove the queries filter on the caller's fabs, but they
/// pass the fabs in themselves. This exercises the leg they stub: that a real
/// token's groups claim survives to the endpoint and becomes that filter.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StreamFabScopingIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>SC-001 — every row of the listing, not merely most of them.</summary>
    [Fact]
    public async Task A_dresden_operator_sees_only_dresden_streams_in_the_listing()
    {
        (Guid inMunich, Guid inDresden) = await ProvisionOnePerFabAsync();

        using HttpClient streams = await StreamsFor(DresdenOperator);
        HttpResponseMessage listed = await streams.GetAsync(
            $"/streams?cameraIdentifiers={inMunich},{inDresden}");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));
        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        rows.EnumerateArray()
            .Select(row => Guid.Parse(row.GetProperty("cameraIdentifier").GetString()!))
            .ShouldBe([inDresden]);
        rows.EnumerateArray()
            .Select(row => row.GetProperty("fab").GetString())
            .ShouldAllBe(fab => fab == "dresden");
    }

    [Fact]
    public async Task A_multi_fab_operator_sees_both_plants()
    {
        (Guid inMunich, Guid inDresden) = await ProvisionOnePerFabAsync();

        using HttpClient streams = await StreamsFor(MultiFabOperator);
        HttpResponseMessage listed = await streams.GetAsync(
            $"/streams?cameraIdentifiers={inMunich},{inDresden}");

        listed.StatusCode.ShouldBe(HttpStatusCode.OK, await BodyAsync(listed));
        JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();

        rows.EnumerateArray()
            .Select(row => row.GetProperty("fab").GetString())
            .ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// SC-002 — compared field by field, not by status alone. A 404 whose body
    /// said "you may not see this stream" would leak precisely what FR-006
    /// withholds, and both responses are 404 either way.
    ///
    /// <para>
    /// Two things are normalised out, and only two: <c>traceId</c>, which
    /// differs per request by design, and the camera identifier the caller
    /// itself supplied, which the two requests cannot share. What is left is
    /// the whole of the response, and it must match byte for byte.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Another_fabs_stream_is_indistinguishable_from_a_camera_with_no_stream()
    {
        (Guid inMunich, _) = await ProvisionOnePerFabAsync();
        Guid neverRegistered = Guid.CreateVersion7();

        using HttpClient streams = await StreamsFor(DresdenOperator);

        HttpResponseMessage hidden = await streams.GetAsync($"/streams/{inMunich}");
        HttpResponseMessage absent = await streams.GetAsync($"/streams/{neverRegistered}");

        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(hidden));
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound, await BodyAsync(absent));

        (await NormalisedProblemAsync(hidden, inMunich))
            .ShouldBe(await NormalisedProblemAsync(absent, neverRegistered));
    }

    /// <summary>
    /// FR-007's neighbour: naming a fab the caller does not hold is 403, not
    /// 404. Nothing is hidden — the caller named a fab, and the answer is about
    /// the fab rather than about any stream in it.
    /// </summary>
    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused()
    {
        using HttpClient streams = await StreamsFor(DresdenOperator);

        HttpResponseMessage refused = await streams.GetAsync(
            $"/streams?fabId=munich&cameraIdentifiers={Guid.CreateVersion7()}");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_is_refused_on_the_single_read()
    {
        using HttpClient streams = await StreamsFor(DresdenOperator);

        HttpResponseMessage refused = await streams.GetAsync(
            $"/streams/{Guid.CreateVersion7()}?fabId=munich");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await BodyAsync(refused));
    }

    private async Task<(Guid InMunich, Guid InDresden)> ProvisionOnePerFabAsync()
    {
        using HttpClient cameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MultiFabOperator, OperatorPassword);

        Guid inMunich = await RegisterAsync(cameras, "munich");
        Guid inDresden = await RegisterAsync(cameras, "dresden");

        using HttpClient owner = await StreamsFor(MultiFabOperator);
        await WaitForStreamAsync(owner, inMunich);
        await WaitForStreamAsync(owner, inDresden);

        return (inMunich, inDresden);
    }

    private Task<HttpClient> StreamsFor(string username) =>
        aspire.CreateAuthenticatedClientAsync("stream-distribution", username, OperatorPassword);

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string fab)
    {
        HttpResponseMessage created = await cameras.PostAsJsonAsync(
            $"/cameras?fabId={fab}",
            new
            {
                name = $"Cam-{Guid.NewGuid():N}"[..12],
                rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
            });

        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task WaitForStreamAsync(HttpClient streams, Guid camera)
    {
        DateTime deadline = DateTime.UtcNow + ProvisionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await streams.GetAsync($"/streams/{camera}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not appear within {ProvisionTimeout.TotalSeconds:F0}s.{Environment.NewLine}" +
            $"stream-distribution log:{Environment.NewLine}{aspire.RecentLogs("stream-distribution")}");
    }

    private static async Task<string> NormalisedProblemAsync(HttpResponseMessage response, Guid requested)
    {
        JsonNode problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        problem.AsObject().Remove("traceId");

        return problem.ToJsonString()
            .Replace(requested.ToString(), "<requested-camera>", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> BodyAsync(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}" +
        $"stream-distribution log:{Environment.NewLine}{aspire.RecentLogs("stream-distribution")}";
}
