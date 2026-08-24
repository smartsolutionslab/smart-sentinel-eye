using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 029 T029 and T030 — the stream follows the camera's corrected address
/// across the context boundary (FR-013, FR-013a, FR-014).
///
/// <para>
/// <b>The assertion is against MediaMTX, not against us.</b> That the
/// announcement was published, that <c>Stream.SourceUrl</c> changed, that the
/// endpoint answered 204 — all three are true while the SFU happily keeps
/// pulling the old address. Only the path's own configured source tells a
/// working re-point from a believed one, which is why this file reads the
/// gateway's config API rather than anything closer to hand.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RepointStreamIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RepointTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private const string CorrectedUrl = "rtsp://camera-sim:8554/corrected";

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Correcting_a_cameras_address_repoints_the_SFU_without_changing_the_path()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        Guid camera = await RegisterAsync(cameras);

        Stream provisioned = await WaitForStreamAsync(camera, ProvisionTimeout);
        string path = provisioned.Path.Value;

        await PatchAddressAsync(cameras, camera, CorrectedUrl);

        // The whole point of the phase: what the SFU is configured to pull.
        await WaitForMediaMtxSourceAsync(path, CorrectedUrl);

        // FR-014 — the path name is unchanged, so a viewer already watching
        // keeps watching. A re-point that tore the path down and re-created it
        // under the same name would pass the source check and still break
        // anyone mid-stream.
        Stream after = await WaitForStreamAsync(camera, RepointTimeout);
        after.Path.Value.ShouldBe(path);
        after.Id.ShouldBe(provisioned.Id, "the same stream moved, rather than a new one appearing");
    }

    /// <summary>
    /// FR-013a. The catalogue records what is true whether or not stream
    /// distribution has caught up — the two are joined by an announcement, not
    /// a shared transaction.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_corrected_even_before_the_stream_catches_up()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        Guid camera = await RegisterAsync(cameras);
        await WaitForStreamAsync(camera, ProvisionTimeout);

        await PatchAddressAsync(cameras, camera, CorrectedUrl);

        // Immediately, without waiting for anything downstream: the catalogue
        // is the authority on the camera and does not consult the SFU.
        HttpResponseMessage read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();

        (await read.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rtspUrl").GetString().ShouldBe(CorrectedUrl);
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras)
    {
        string name = $"repoint-{Guid.CreateVersion7():N}"[..24];

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = "rtsp://camera-sim:8554/original",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task PatchAddressAsync(HttpClient cameras, Guid camera, string rtspUrl)
    {
        HttpResponseMessage read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();
        int version = (await read.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();

        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = new StringContent($"{{\"rtspUrl\":\"{rtspUrl}\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));

        (await cameras.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<Stream> WaitForStreamAsync(Guid camera, TimeSpan timeout)
    {
        CameraIdentifier cameraId = CameraIdentifier.From(camera);
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await using StreamDistributionDbContext context =
                await aspire.CreateStreamDistributionDbContextAsync();

            Stream? row = await context.Streams
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Camera == cameraId);

            if (row is not null)
            {
                return row;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not appear within {timeout.TotalSeconds:F0}s.");
    }

    /// <summary>
    /// Reads the SFU's own view. Asserting anything on our side of the boundary
    /// would pass while MediaMTX still pulled the old address.
    /// </summary>
    private async Task WaitForMediaMtxSourceAsync(string path, string expectedSource)
    {
        using HttpClient mediamtx = aspire.App.CreateHttpClient("mediamtx", "api");
        DateTime deadline = DateTime.UtcNow + RepointTimeout;
        string observed = "(never read)";

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await mediamtx.GetAsync($"/v3/config/paths/get/{path}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement configured = await response.Content.ReadFromJsonAsync<JsonElement>();

                if (configured.TryGetProperty("source", out JsonElement source))
                {
                    observed = source.GetString() ?? "(null)";

                    if (observed == expectedSource)
                    {
                        return;
                    }
                }
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"MediaMTX path {path} was still pulling '{observed}' rather than '{expectedSource}' "
            + $"after {RepointTimeout.TotalSeconds:F0}s — the catalogue and the SFU disagree.");
    }
}
