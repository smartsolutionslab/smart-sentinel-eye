using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 002 T077 — drives the cameras-list health badge use case. Registers
/// five cameras and asserts <c>GET /streams?cameraIdentifiers=...</c>
/// returns one DTO per identifier with a non-Provisioning state within 30
/// seconds (the SLO for "the badge has settled by the time the user sees
/// the row"). All cameras use unreachable RTSP URLs in CI — they settle on
/// <c>Degraded</c> after the StreamHealthWatcher polls MediaMTX twice.
/// Healthy-state assertions require an RTSP test source and are deferred
/// to the polish phase (T086).
/// </summary>
[Collection(AspireCollection.Name)]
public class ListStreamsByCamerasIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Five_cameras_settle_into_observable_states_in_the_batch_API_within_30_seconds()
    {
        using HttpClient cameraClient = await aspire.CreateAdminClientAsync("camera-catalog");
        using HttpClient streamClient = await aspire.CreateAdminClientAsync("stream-distribution");

        Guid[] cameras = new Guid[5];
        for (int index = 0; index < cameras.Length; index++)
        {
            cameras[index] = await RegisterAsync(
                cameraClient,
                $"Cam-Badge-{index}",
                $"rtsp://10.0.6.{index + 1}/h264");
        }

        await WaitForBatchSettledAsync(streamClient, cameras, SettleTimeout);

        HttpResponseMessage response = await streamClient.GetAsync(
            $"/streams?cameraIdentifiers={string.Join(',', cameras)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        JsonElement items = await response.Content.ReadFromJsonAsync<JsonElement>();
        items.GetArrayLength().ShouldBe(cameras.Length);

        Dictionary<Guid, string> stateByCamera = items.EnumerateArray().ToDictionary(
            element => Guid.Parse(element.GetProperty("cameraIdentifier").GetString()!),
            element => element.GetProperty("state").GetString()!);

        foreach (Guid camera in cameras)
        {
            stateByCamera.ShouldContainKey(camera);
            stateByCamera[camera].ShouldBe("Degraded");
        }
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, string rtspUrl)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras", new { name, rtspUrl });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task WaitForBatchSettledAsync(
        HttpClient streamClient, Guid[] cameras, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        string query = string.Join(',', cameras);

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await streamClient.GetAsync(
                $"/streams?cameraIdentifiers={query}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement items = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (items.GetArrayLength() == cameras.Length &&
                    items.EnumerateArray().All(IsNonProvisioning))
                {
                    return;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Streams for {cameras.Length} cameras did not all leave Provisioning within {timeout.TotalSeconds:F0}s.");

        static bool IsNonProvisioning(JsonElement entry) =>
            entry.GetProperty("state").GetString() is string state &&
            !string.Equals(state, "Provisioning", StringComparison.Ordinal);
    }
}
