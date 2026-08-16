using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 016 T012 — SC-003, over the real stack: every stream provisioned after
/// this change carries the fab of its camera.
///
/// <para>
/// The handler tests prove the derivation reads <c>Metadata.Fab</c>, but they
/// construct that message themselves. This exercises the leg they stub — that
/// CameraCatalog actually puts the fab on the event it publishes, and that it
/// survives RabbitMQ and the Wolverine subscriber to reach the streams table.
/// </para>
///
/// <para>
/// Read back through the DbContext rather than the streams API: the reads are
/// not fab-aware until Phase 4, and a test of the derivation should not depend
/// on scoping that does not exist yet.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StreamFabDerivationIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Both fabs in one test on purpose. A derivation that hard-coded munich,
    /// or fell back to it, would pass the munich half — only the dresden half
    /// can fail.
    /// </summary>
    [Fact]
    public async Task A_stream_carries_the_fab_of_the_camera_it_serves()
    {
        using HttpClient cameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MultiFabOperator, OperatorPassword);

        Guid inMunich = await RegisterAsync(cameras, "munich");
        Guid inDresden = await RegisterAsync(cameras, "dresden");

        (await WaitForFabAsync(inMunich)).ShouldBe("munich");
        (await WaitForFabAsync(inDresden)).ShouldBe("dresden");
    }

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

    private async Task<string> WaitForFabAsync(Guid camera)
    {
        CameraIdentifier cameraId = CameraIdentifier.From(camera);
        DateTime deadline = DateTime.UtcNow + ProvisionTimeout;

        while (DateTime.UtcNow < deadline)
        {
            await using StreamDistributionDbContext context =
                await aspire.CreateStreamDistributionDbContextAsync();

            Stream? provisioned = await context.Streams
                .AsNoTracking()
                .SingleOrDefaultAsync(stream => stream.Camera == cameraId);

            if (provisioned is not null)
            {
                provisioned.Fab.ShouldNotBeNull(
                    $"Stream for camera {camera} was provisioned without a fab.");
                return provisioned.Fab.Value;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No stream was provisioned for camera {camera} within {ProvisionTimeout.TotalSeconds:F0}s.{Environment.NewLine}" +
            $"stream-distribution log:{Environment.NewLine}{aspire.RecentLogs("stream-distribution")}");
    }
}
