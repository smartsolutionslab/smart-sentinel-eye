using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 016 T025 — SC-004 and the only test of FR-009.
///
/// <para>
/// Between the migration and the first attribution pass every stream has a
/// null fab. That window is deliberate (research.md §2) and it must fail
/// closed: an unattributed stream is shown to <em>nobody</em>, not to its own
/// fab's operator and not to a multi-fab one.
/// </para>
///
/// <para>
/// This is the case that is invisible when it works — nothing observable
/// happens — which is exactly why it is written down. The alternative, an
/// unattributed stream shown to everyone, is the defect this feature removes
/// reappearing as a transitional state.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StreamFabAttributionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    private static readonly TimeSpan ProvisionTimeout = TimeSpan.FromSeconds(30);

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_stream_with_no_fab_is_returned_to_nobody()
    {
        Guid camera = await ProvisionInMunichAsync();
        await BlankTheFabAsync(camera);

        // Its own fab's operator first — the tempting special case is to treat
        // a null fab as "belongs to everyone here", and this is where that
        // would show.
        using HttpClient munich = await aspire.CreateAdminClientAsync("stream-distribution");
        (await streamsVisibleTo(munich, camera)).ShouldBeFalse();
        (await munich.GetAsync($"/streams/{camera}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // And a multi-fab operator, who holds every fab there is and still
        // must not see a stream that belongs to none of them.
        using HttpClient both = await aspire.CreateAuthenticatedClientAsync(
            "stream-distribution", MultiFabOperator, OperatorPassword);
        (await streamsVisibleTo(both, camera)).ShouldBeFalse();
        (await both.GetAsync($"/streams/{camera}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        static async Task<bool> streamsVisibleTo(HttpClient client, Guid camera)
        {
            HttpResponseMessage listed = await client.GetAsync($"/streams?cameraIdentifiers={camera}");
            listed.StatusCode.ShouldBe(HttpStatusCode.OK, await listed.Content.ReadAsStringAsync());

            JsonElement rows = await listed.Content.ReadFromJsonAsync<JsonElement>();
            return rows.GetArrayLength() > 0;
        }
    }

    /// <summary>
    /// The other half of the same guarantee: blanking the fab is what makes it
    /// invisible, so the stream must be visible before that. Without this the
    /// test above would pass against a listing that was simply broken.
    /// </summary>
    [Fact]
    public async Task The_same_stream_is_visible_while_it_still_has_its_fab()
    {
        Guid camera = await ProvisionInMunichAsync();

        using HttpClient munich = await aspire.CreateAdminClientAsync("stream-distribution");
        HttpResponseMessage read = await munich.GetAsync($"/streams/{camera}");

        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        JsonElement stream = await read.Content.ReadFromJsonAsync<JsonElement>();
        stream.GetProperty("fab").GetString().ShouldBe("munich");
    }

    private async Task<Guid> ProvisionInMunichAsync()
    {
        using HttpClient cameras = await aspire.CreateAdminClientAsync("camera-catalog");

        HttpResponseMessage created = await cameras.PostAsJsonAsync(
            "/cameras",
            new
            {
                name = $"Cam-{Guid.NewGuid():N}"[..12],
                rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
            });
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        Guid camera = await created.Content.ReadFromJsonAsync<Guid>();

        using HttpClient streams = await aspire.CreateAdminClientAsync("stream-distribution");
        DateTime deadline = DateTime.UtcNow + ProvisionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await streams.GetAsync($"/streams/{camera}")).StatusCode == HttpStatusCode.OK)
            {
                return camera;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not appear within {ProvisionTimeout.TotalSeconds:F0}s.{Environment.NewLine}" +
            $"stream-distribution log:{Environment.NewLine}{aspire.RecentLogs("stream-distribution")}");
    }

    /// <summary>
    /// Recreates a row that predates the fab column. Written through SQL
    /// rather than the aggregate because the aggregate deliberately cannot
    /// express it — <c>Provision</c> requires a fab and there is no setter.
    /// </summary>
    private async Task BlankTheFabAsync(Guid camera)
    {
        await using StreamDistributionDbContext context =
            await aspire.CreateStreamDistributionDbContextAsync();

        await context.Database.ExecuteSqlAsync(
            $"UPDATE streams SET fab = NULL WHERE camera_id = {camera}");
    }
}
