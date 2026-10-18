using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

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
        using var cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        var camera = await RegisterAsync(cameras);

        var provisioned = await WaitForStreamAsync(camera, ProvisionTimeout);
        var path = provisioned.Path.Value;

        await PatchAddressAsync(cameras, camera, CorrectedUrl);

        // The whole point of the phase: what the SFU is configured to pull.
        await WaitForMediaMtxSourceAsync(path, CorrectedUrl);

        // FR-014 — the path name is unchanged, so a viewer already watching
        // keeps watching. A re-point that tore the path down and re-created it
        // under the same name would pass the source check and still break
        // anyone mid-stream.
        var after = await WaitForStreamAsync(camera, RepointTimeout);
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
        using var cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        var camera = await RegisterAsync(cameras);
        await WaitForStreamAsync(camera, ProvisionTimeout);

        await PatchAddressAsync(cameras, camera, CorrectedUrl);

        // Immediately, without waiting for anything downstream: the catalogue
        // is the authority on the camera and does not consult the SFU.
        var read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();

        (await read.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rtspUrl").GetString().ShouldBe(CorrectedUrl);
    }

    /// <summary>
    /// Spec 385. The stream records the instant of the address change it last
    /// applied, so a retried older change can be recognised and ignored. Read
    /// with raw SQL: the column is not on any type this project can name yet.
    /// </summary>
    [Fact]
    public async Task Correcting_a_cameras_address_records_when_the_change_was_made()
    {
        using var cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        var camera = await RegisterAsync(cameras);
        var provisioned = await WaitForStreamAsync(camera, ProvisionTimeout);

        var before = DateTimeOffset.UtcNow;
        await PatchAddressAsync(cameras, camera, CorrectedUrl);

        // The save precedes the gateway call, so once the SFU reports the new
        // source the row has been written.
        await WaitForMediaMtxSourceAsync(provisioned.Path.Value, CorrectedUrl);
        var after = DateTimeOffset.UtcNow;

        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
        var recorded = await context.Database
            .SqlQuery<DateTimeOffset?>($"""
                SELECT source_changed_at AS "Value"
                FROM streams
                WHERE camera_id = {camera}
                """)
            .ToListAsync();

        var changedAt = recorded.ShouldHaveSingleItem();
        changedAt.ShouldNotBeNull();
        changedAt.Value.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        changedAt.Value.ShouldBeLessThanOrEqualTo(after.AddSeconds(1));
    }

    /// <summary>
    /// Spec 403. The stream records the camera version the change was made
    /// against, which is the <c>If-Match</c> the PATCH named. The expected value
    /// is the version the server issued on <c>GET</c>, not a number this test
    /// chose, so a column filled with a constant or with the committed version
    /// (one more) would not pass. Read with raw SQL for the reason above.
    /// </summary>
    [Fact]
    public async Task Correcting_a_cameras_address_records_the_camera_version_it_was_made_against()
    {
        using var cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        var camera = await RegisterAsync(cameras);
        var provisioned = await WaitForStreamAsync(camera, ProvisionTimeout);

        var read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();
        var version = (await read.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32();

        HttpRequestMessage request = new(HttpMethod.Patch, $"/cameras/{camera}")
        {
            Content = new StringContent($"{{\"rtspUrl\":\"{CorrectedUrl}\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        (await cameras.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await WaitForMediaMtxSourceAsync(provisioned.Path.Value, CorrectedUrl);

        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
        var recorded = await context.Database
            .SqlQuery<int?>($"""
                SELECT source_camera_version AS "Value"
                FROM streams
                WHERE camera_id = {camera}
                """)
            .ToListAsync();

        recorded.ShouldHaveSingleItem().ShouldBe(version);
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras)
    {
        var name = $"repoint-{Guid.CreateVersion7():N}"[..24];

        var created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = "rtsp://camera-sim:8554/original",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task PatchAddressAsync(HttpClient cameras, Guid camera, string rtspUrl)
    {
        var read = await cameras.GetAsync($"/cameras/{camera}");
        read.EnsureSuccessStatusCode();
        var version = (await read.Content.ReadFromJsonAsync<JsonElement>())
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
        var cameraId = CameraIdentifier.From(camera);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await using var context =
                await aspire.CreateStreamDistributionDbContextAsync();

            var row = await context.Streams
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
        using var mediamtx = aspire.CreateMediaMtxApiClient();
        var deadline = DateTime.UtcNow + RepointTimeout;
        var observed = "(never read)";

        while (DateTime.UtcNow < deadline)
        {
            var response = await mediamtx.GetAsync($"/v3/config/paths/get/{path}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var configured = await response.Content.ReadFromJsonAsync<JsonElement>();

                if (configured.TryGetProperty("source", out var source))
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
