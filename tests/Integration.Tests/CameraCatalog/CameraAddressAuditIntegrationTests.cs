using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 029 T032 — every correction is attributable, and only real corrections
/// are recorded (FR-011).
///
/// <para>
/// The no-op case is the one worth having. From the endpoint, re-submitting the
/// address a camera already has and genuinely changing it both answer 204; the
/// difference shows only here, as a row that should not exist.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CameraAddressAuditIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string OperatorPassword = "Operator1234";
    private const string CorrectedUrl = "rtsp://10.0.5.44/h264";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_correction_is_audited_once_and_names_the_operator()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);

        await CorrectAsync(cameras, camera, CorrectedUrl);

        (Guid Actor, string PreviousUrl, string Url) audited = await PollForAuditAsync(camera);

        audited.Url.ShouldBe(CorrectedUrl);
        audited.PreviousUrl.ShouldNotBe(CorrectedUrl, "the trail records what changed, not merely that it did");

        // Not the system actor. If the metadata lost the operator the row would
        // fall back to it, and the trail would say the system corrected a
        // camera nobody asked it to.
        audited.Actor.ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    /// Idempotency as no event, not no error — spec 028's lesson, in the place
    /// it hides. A second correction to the same address answers 204 exactly as
    /// the first did.
    /// </summary>
    [Fact]
    public async Task Re_submitting_the_same_address_adds_no_second_audit_row()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras);

        await CorrectAsync(cameras, camera, CorrectedUrl);
        await PollForAuditAsync(camera);

        // Same address again. 204 either way, so the endpoint cannot tell us
        // whether this was a no-op.
        await CorrectAsync(cameras, camera, CorrectedUrl);

        // Settle: a duplicate arrives after the first, so counting immediately
        // would be exactly the wrong moment to stop looking.
        await Task.Delay(TimeSpan.FromSeconds(3));

        (await CountAsync(camera)).ShouldBe(1,
            "a correction that changed nothing must not announce, or the trail records a change that never happened");
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static async Task CorrectAsync(HttpClient cameras, Guid camera, string rtspUrl)
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

    private async Task<(Guid Actor, string PreviousUrl, string Url)> PollForAuditAsync(Guid camera)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            await using AuditObservabilityDbContext context =
                await aspire.CreateAuditObservabilityDbContextAsync();

            List<string> rows = await context.Database
                .SqlQuery<string>($"""
                    SELECT actor_identifier::text || '|' || (payload->>'PreviousUrl') || '|' || (payload->>'Url') AS "Value"
                    FROM audit_events
                    WHERE event_kind = 'CameraAddressChangedV1'
                      AND payload->>'Camera' = {camera.ToString()}
                    """)
                .ToListAsync();

            if (rows.Count > 0)
            {
                string[] parts = rows[0].Split('|');
                return (Guid.Parse(parts[0]), parts[1], parts[2]);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException($"No CameraAddressChangedV1 audit row for camera {camera} within 30s.");
    }

    private async Task<int> CountAsync(Guid camera)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = 'CameraAddressChangedV1'
                  AND payload->>'Camera' = {camera.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }

    private static async Task<Guid> RegisterAsync(HttpClient cameras)
    {
        string name = $"audit-{Guid.CreateVersion7():N}"[..24];

        HttpResponseMessage created = await cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = "rtsp://10.0.5.12/h264",
        });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }
}
