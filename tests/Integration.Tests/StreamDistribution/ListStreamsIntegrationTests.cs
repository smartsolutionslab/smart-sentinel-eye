using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// GET /streams batch endpoint (FR-006). Mirrors the camera-list health
/// badge use case: the management UI passes the visible camera IDs and
/// expects one DTO per camera that has a provisioned stream.
/// </summary>
[Collection(AspireCollection.Name)]
public class ListStreamsIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_returns_one_entry_per_provisioned_camera()
    {
        using var cameraClient = await aspire.CreateAdminClientAsync("camera-catalog");
        using var streamClient = await aspire.CreateAdminClientAsync("stream-distribution");

        var camera1 = await RegisterAsync(cameraClient, "Cam-Batch-1", "rtsp://10.0.5.1/h264");
        var camera2 = await RegisterAsync(cameraClient, "Cam-Batch-2", "rtsp://10.0.5.2/h264");

        await WaitForStreamAsync(streamClient, camera1, ProvisionTimeout);
        await WaitForStreamAsync(streamClient, camera2, ProvisionTimeout);

        var response = await streamClient.GetAsync($"/streams?cameraIdentifiers={camera1},{camera2}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        items.GetArrayLength().ShouldBe(2);
        var returnedIds = items.EnumerateArray()
            .Select(e => Guid.Parse(e.GetProperty("cameraIdentifier").GetString()!));
        returnedIds.ShouldContain(camera1);
        returnedIds.ShouldContain(camera2);
    }

    [Fact]
    public async Task List_omits_cameras_that_have_no_stream_yet()
    {
        using var streamClient = await aspire.CreateAdminClientAsync("stream-distribution");

        var unknownCamera = Guid.CreateVersion7();
        var response = await streamClient.GetAsync(
            $"/streams?cameraIdentifiers={unknownCamera}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement>();
        items.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task List_without_a_token_returns_401()
    {
        var response = await aspire.StreamDistribution.GetAsync(
            $"/streams?cameraIdentifiers={Guid.CreateVersion7()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, string rtspUrl)
    {
        var response = await client.PostAsJsonAsync(
            "/cameras", new { name, rtspUrl });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task WaitForStreamAsync(HttpClient streamClient, Guid camera, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await streamClient.GetAsync($"/streams/{camera}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        throw new TimeoutException($"Stream for camera {camera} did not appear within {timeout.TotalSeconds:F0}s.");
    }
}
