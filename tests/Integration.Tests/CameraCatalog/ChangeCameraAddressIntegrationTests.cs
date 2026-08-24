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

        // The same version again — now stale.
        HttpResponseMessage replayed = await PatchAsync(cameras, camera, "rtsp://10.0.5.99/h264", version);

        replayed.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

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
            rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
