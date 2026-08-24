using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 029 T009 — reading one camera over real HTTP (#1435).
///
/// <para>
/// The version assertions are the point of this file. Nothing exposed a
/// camera's version before this feature, and the edit cannot be built until
/// something does — so "the ETag and the body agree, and both are the
/// aggregate's own" is what makes US2 possible rather than a detail of US1.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class GetCameraIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Reading_one_camera_returns_it_with_a_version_on_both_the_ETag_and_the_body()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras, UniqueName());

        HttpResponseMessage response = await cameras.GetAsync($"/cameras/{camera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("cameraIdentifier").GetGuid().ShouldBe(camera);
        body.GetProperty("fab").GetString().ShouldBe("munich");
        body.GetProperty("status").GetString().ShouldBe("Registered");

        int version = body.GetProperty("version").GetInt32();

        // The two have to agree, because the caller may take either and the
        // edit will refuse a version that is not current.
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.ETag.Tag.ShouldBe($"\"{version}\"");
    }

    /// <summary>
    /// FR-002. A retired camera leaves the default listing (spec 028 FR-007)
    /// but stays readable — the record outlives the hardware, and the audit
    /// trail refers to it.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_is_still_readable_and_says_so()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras, UniqueName());

        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage response = await cameras.GetAsync($"/cameras/{camera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString().ShouldBe("Decommissioned");
    }

    /// <summary>
    /// FR-006 over the wire. The full field-by-field comparison across both
    /// endpoints is US3's job (T018–T021); this is the read-side half, asserted
    /// here so US1 does not ship a leak while waiting for it.
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich, UniqueName());

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage crossFab = await dresden.GetAsync($"/cameras/{inMunich}");
        HttpResponseMessage neverExisted = await dresden.GetAsync($"/cameras/{Guid.CreateVersion7()}");

        crossFab.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        neverExisted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        JsonElement crossFabProblem = await crossFab.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement unknownProblem = await neverExisted.Content.ReadFromJsonAsync<JsonElement>();

        crossFabProblem.GetProperty("title").GetString()
            .ShouldBe(unknownProblem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Every_row_of_the_listing_carries_a_version()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        await RegisterAsync(cameras, UniqueName());

        HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200");
        listed.EnsureSuccessStatusCode();

        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        // The whole reason the version is on the body and not only the ETag:
        // an operator can correct a camera straight from the listing.
        page.GetProperty("items").EnumerateArray()
            .ShouldAllBe(row => row.GetProperty("version").GetInt32() >= 0);

        page.GetProperty("items").EnumerateArray().ShouldNotBeEmpty();
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static string UniqueName() => $"read-{Guid.CreateVersion7():N}"[..24];

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string name)
    {
        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
