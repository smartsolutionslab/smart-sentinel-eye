using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 028 T027, T028 and T031 — the stream follows the camera across the
/// context boundary (FR-008), and the health sweep stops looking (research §4).
///
/// <para>
/// The two contexts are joined by an announcement, not a transaction (FR-008a),
/// so every assertion here waits rather than reads once.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RetireStreamIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetireTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// T027. All three halves of FR-008 in one pass, because they are one
    /// outcome: the path is gone, the aggregate is terminal, and the row is
    /// still there. Asserting only the first two would pass against an
    /// implementation that deleted the stream, which FR-008 explicitly refuses —
    /// retirement records that hardware <em>was</em> there.
    /// </summary>
    [Fact]
    public async Task Retiring_a_camera_removes_its_path_and_leaves_the_stream_terminal_and_present()
    {
        Guid camera = await RegisterAsync("Line-9-Retire");
        Stream provisioned = await WaitForStreamRowAsync(camera, ProvisionTimeout);

        await AssertMediaMtxPathAsync(provisioned.Path.Value, expected: true);

        await RetireAsync(camera);

        Stream retired = await WaitForStateAsync(camera, StreamState.Retired, RetireTimeout);

        retired.Id.ShouldBe(provisioned.Id, "the same stream reached the terminal state, not a new one");

        await AssertMediaMtxPathAsync(provisioned.Path.Value, expected: false);

        await using StreamDistributionDbContext context =
            await aspire.CreateStreamDistributionDbContextAsync();
        CameraIdentifier cameraId = CameraIdentifier.From(camera);

        (await context.Streams.AsNoTracking().CountAsync(row => row.Camera == cameraId))
            .ShouldBe(1, "the row is kept — retirement records that the hardware was there (FR-008)");
    }

    /// <summary>
    /// T028 / FR-008a. The camera is retired whether or not stream distribution
    /// can complete its teardown. Provisioning deliberately points at an
    /// unreachable RTSP source, so the path exists in MediaMTX but never carries
    /// a frame — the camera's retirement must not wait on, or be undone by,
    /// anything happening downstream of it.
    /// </summary>
    [Fact]
    public async Task A_camera_is_retired_even_when_its_stream_never_became_healthy()
    {
        Guid camera = await RegisterAsync("Line-9-Unreachable");
        await WaitForStreamRowAsync(camera, ProvisionTimeout);

        await RetireAsync(camera);

        // The catalogue is the authority on the camera and does not consult
        // stream distribution: this is true immediately, not eventually.
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");
        HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200&includeRetired=true");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        page.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("cameraIdentifier").GetGuid() == camera)
            .GetProperty("status").GetString()
            .ShouldBe("Decommissioned");

        // And the stream still follows, unreachable source notwithstanding.
        await WaitForStateAsync(camera, StreamState.Retired, RetireTimeout);
    }

    /// <summary>
    /// T031 (research §4), and the reason Phase 5 is non-optional. Since #1801
    /// the watcher announces <em>every</em> health change rather than one per
    /// sweep, so a retired stream still being probed would fail its probe
    /// forever and become a permanent source of announcements and audit rows for
    /// hardware that does not exist.
    ///
    /// <para>
    /// Asserted over a window rather than once: the watcher polls every two
    /// seconds, so a single read straight after retirement would pass simply by
    /// landing between sweeps. The baseline is taken once the stream is terminal
    /// and compared against the same count several sweeps later.
    /// </para>
    /// </summary>
    [Fact]
    public async Task After_retirement_no_further_health_changes_are_announced_for_that_camera()
    {
        Guid camera = await RegisterAsync("Line-9-Quiet");
        await WaitForStreamRowAsync(camera, ProvisionTimeout);

        await RetireAsync(camera);
        await WaitForStateAsync(camera, StreamState.Retired, RetireTimeout);

        // Let anything already in flight land before the baseline is taken.
        await Task.Delay(TimeSpan.FromSeconds(3));
        int baseline = await HealthAnnouncementCountAsync(camera);

        // Several watcher periods — StreamHealthWatcher polls every 2 s.
        await Task.Delay(TimeSpan.FromSeconds(10));

        (await HealthAnnouncementCountAsync(camera)).ShouldBe(baseline,
            "a retired stream is excluded from the sweep, so it cannot announce again");
    }

    private async Task<Guid> RegisterAsync(string name)
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");

        HttpResponseMessage register = await cameras.PostAsJsonAsync(
            "/cameras",
            new
            {
                name = $"{name}-{Guid.CreateVersion7():N}"[..28],
                rtspUrl = "rtsp://unreachable.test/h264",
            });

        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await register.Content.ReadFromJsonAsync<Guid>();
    }

    private async Task RetireAsync(Guid camera)
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");

        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task<Stream> WaitForStreamRowAsync(Guid camera, TimeSpan timeout) =>
        WaitForAsync(camera, _ => true, timeout, "appear");

    private Task<Stream> WaitForStateAsync(Guid camera, StreamState state, TimeSpan timeout) =>
        WaitForAsync(camera, row => row.State == state, timeout, $"reach {state.Value}");

    private async Task<Stream> WaitForAsync(
        Guid camera,
        Func<Stream, bool> until,
        TimeSpan timeout,
        string what)
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

            if (row is not null && until(row))
            {
                return row;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not {what} within {timeout.TotalSeconds:F0}s.");
    }

    private async Task<int> HealthAnnouncementCountAsync(Guid camera)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = 'StreamHealthChangedV1'
                  AND payload->>'Camera' = {camera.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }

    private async Task AssertMediaMtxPathAsync(string path, bool expected)
    {
        using HttpClient mediamtx = aspire.App.CreateHttpClient("mediamtx", "api");
        DateTime deadline = DateTime.UtcNow + RetireTimeout;

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await mediamtx.GetAsync($"/v3/config/paths/get/{path}");

            if ((response.StatusCode == HttpStatusCode.OK) == expected)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"MediaMTX path {path} was still {(expected ? "absent" : "present")} after {RetireTimeout.TotalSeconds:F0}s.");
    }
}
