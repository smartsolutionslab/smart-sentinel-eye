using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 029 T017 — correcting a camera's address over real HTTP (#1435).
///
/// <para>
/// The concurrency cases are the substance. A change that did not have to say
/// what it was based on would reopen the lost-update hole ADR-0113 closes, and
/// both of the ways that regresses — accepting a stale version, or accepting a
/// missing one — answer 204 and look correct from the caller's side.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class ChangeCameraAddressIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string OperatorPassword = "Operator1234";
    private const string CorrectedUrl = "rtsp://10.0.5.44/h264";

    /// <summary>
    /// The address every camera in this class is registered with, and it must
    /// never equal <see cref="CorrectedUrl"/>.
    ///
    /// <para>
    /// It used to be <c>rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264</c>,
    /// which draws 44 about once in 248 runs — and 44 is
    /// <see cref="CorrectedUrl"/>. On those runs the first correction submitted
    /// an address the camera already had, which spec 029 makes an idempotent
    /// no-op: it succeeds, raises no event, and <b>does not advance the
    /// version</b>. The version the stale-version test then replayed was still
    /// current, so the server answered <c>204</c> instead of <c>412</c> and the
    /// test failed for a reason that had nothing to do with concurrency.
    /// </para>
    ///
    /// <para>
    /// A constant is the fix because the randomness bought nothing: names are
    /// already unique per registration, and no test here reads the address
    /// expecting variety.
    /// </para>
    /// </summary>
    private const string OriginalUrl = "rtsp://10.0.7.12/h264";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Correcting_the_address_stores_it_and_advances_the_version()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);
        int version = await VersionOfAsync(cameras, camera);

        HttpResponseMessage changed = await PatchAsync(cameras, camera, CorrectedUrl, version);

        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement after = await ReadAsync(cameras, camera);
        after.GetProperty("rtspUrl").GetString().ShouldBe(CorrectedUrl);

        // The version has to move, or a second change could quote the first
        // version and succeed — which is the lost update the scheme prevents.
        after.GetProperty("version").GetInt32().ShouldBeGreaterThan(version);
    }

    [Fact]
    public async Task A_stale_version_is_refused_and_the_address_is_untouched()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);
        int version = await VersionOfAsync(cameras, camera);

        (await PatchAsync(cameras, camera, CorrectedUrl, version))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The premise, asserted rather than assumed: the correction above has
        // to have *moved* the version, or the "stale" version replayed below is
        // still current and this test proves nothing while appearing to.
        //
        // Not hypothetical. The registration address used to be random and could
        // land on CorrectedUrl, making that first correction an idempotent
        // no-op that leaves the version where it was — a roughly 1-in-248
        // failure that reported itself as "expected 412, got 204" and sent the
        // reader hunting through the concurrency code.
        (await VersionOfAsync(cameras, camera)).ShouldNotBe(version);

        // The same version again — now stale.
        HttpResponseMessage replayed = await PatchAsync(cameras, camera, "rtsp://10.0.5.99/h264", version);

        replayed.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // The code, over the wire. Spec 031 made the code authoritative and the
        // status irrelevant (ADR-0119), so a test asserting only the status now
        // asserts the part that no longer decides anything. The client reads
        // ProblemDetails.title, which is where ApiErrorResults.ToProblem puts
        // the code — if that mapping broke, the handler unit test would still
        // pass and every operator would still be told the wrong thing.
        JsonElement problem = JsonDocument.Parse(await replayed.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("CAMERA_VERSION_STALE");

        // FR-010: a rejected change leaves the camera exactly as it was.
        (await ReadAsync(cameras, camera)).GetProperty("rtspUrl").GetString().ShouldBe(CorrectedUrl);
    }

    /// <summary>
    /// 428, not a silent success. A missing precondition must not fall back to
    /// "no concurrency control" — that is the hole ADR-0113 closes, and it
    /// would be invisible from the caller's side.
    /// </summary>
    [Fact]
    public async Task A_change_without_an_If_Match_is_refused()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);

        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = Body(CorrectedUrl),
        };

        HttpResponseMessage response = await cameras.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await ReadAsync(cameras, camera)).GetProperty("rtspUrl").GetString().ShouldNotBe(CorrectedUrl);
    }

    /// <summary>
    /// FR-005. Retirement is terminal, so a corrected address for hardware that
    /// is gone describes nothing. The refusal originates in the aggregate; this
    /// asserts it survives the whole stack.
    /// </summary>
    [Fact]
    public async Task A_retired_cameras_address_cannot_be_changed()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);

        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        int version = await VersionOfAsync(cameras, camera);

        (await PatchAsync(cameras, camera, CorrectedUrl, version))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unusable_address_is_refused_and_the_stored_one_is_unchanged()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);
        int version = await VersionOfAsync(cameras, camera);
        string original = (await ReadAsync(cameras, camera)).GetProperty("rtspUrl").GetString()!;

        (await PatchAsync(cameras, camera, "http://not-rtsp.example/stream", version))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ReadAsync(cameras, camera)).GetProperty("rtspUrl").GetString().ShouldBe(original);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static StringContent Body(string rtspUrl) =>
        new($"{{\"rtspUrl\":\"{rtspUrl}\"}}", Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PatchAsync(
        HttpClient cameras, Guid camera, string rtspUrl, int expectedVersion)
    {
        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = Body(rtspUrl),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{expectedVersion}\""));

        return await cameras.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient cameras, Guid camera)
    {
        HttpResponseMessage response = await cameras.GetAsync($"/cameras/{camera}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> VersionOfAsync(HttpClient cameras, Guid camera) =>
        (await ReadAsync(cameras, camera)).GetProperty("version").GetInt32();

    private static async Task<Guid> RegisterAsync(HttpClient cameras)
    {
        string name = $"edit-{Guid.CreateVersion7():N}"[..24];

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = OriginalUrl,
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
