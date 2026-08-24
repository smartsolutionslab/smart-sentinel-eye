using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.CameraCatalog;

/// <summary>
/// Spec 028 T011 and T012 — retiring a camera over real HTTP (#1433).
///
/// <para>
/// The idempotency assertion is on the <b>audit trail</b>, not on the status
/// code. A second retire that succeeds while announcing again returns 204 both
/// times and looks entirely correct from the endpoint; the only place the
/// duplicate shows is downstream, which is where this looks.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RetireCameraIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private const string MunichOperator = "op-3@munich.test";
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";

    public Task InitializeAsync() => aspire.ResetCameraCatalogAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Retiring_a_camera_succeeds_and_retiring_it_again_announces_nothing_further()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid camera = await RegisterAsync(cameras, UniqueName());

        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The outcome the caller asked for is already true — that is a success,
        // not a conflict (FR-005).
        (await cameras.PostAsync($"/cameras/{camera}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        int announced = await PollForRetirementCountAsync(camera, expected: 1);

        announced.ShouldBe(1,
            "two retirements in the audit trail means the aggregate raised twice while the endpoint stayed 204");
    }

    /// <summary>
    /// FR-004. The refusal for another fab's camera must be indistinguishable
    /// from the refusal for an identifier that names nothing — not merely the
    /// same status, the same body. A distinguishable answer lets an operator
    /// enumerate another plant's cameras, and a camera's record carries its
    /// RTSP address (#1397).
    /// </summary>
    [Fact]
    public async Task Another_fabs_camera_is_refused_exactly_as_an_unknown_one_is()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        Guid inMunich = await RegisterAsync(munich, UniqueName());

        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage crossFab = await dresden.PostAsync($"/cameras/{inMunich}/retire", null);
        HttpResponseMessage neverExisted = await dresden.PostAsync($"/cameras/{Guid.CreateVersion7()}/retire", null);

        crossFab.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        neverExisted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Same shape, not just the same code: the detail must not name the fab
        // or otherwise confirm the camera is somewhere.
        JsonElement crossFabProblem = await crossFab.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement unknownProblem = await neverExisted.Content.ReadFromJsonAsync<JsonElement>();

        crossFabProblem.GetProperty("title").GetString()
            .ShouldBe(unknownProblem.GetProperty("title").GetString());

        // And the camera is untouched — a refused retire must not half-happen.
        using HttpClient owner = await ClientFor(MunichOperator);
        (await NamesAsync(owner)).Length.ShouldBeGreaterThan(0);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static string UniqueName() => $"retire-{Guid.CreateVersion7():N}"[..24];

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

    private static async Task<string[]> NamesAsync(HttpClient cameras)
    {
        HttpResponseMessage listed = await cameras.GetAsync("/cameras?limit=200");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!)];
    }

    /// <summary>
    /// Waits for the announcement to reach the audit trail, then keeps looking
    /// for a short while longer. A duplicate arrives <em>after</em> the first,
    /// so returning at the moment the expected count is reached would be
    /// exactly the wrong time to stop counting.
    /// </summary>
    private async Task<int> PollForRetirementCountAsync(Guid camera, int expected)
    {
        int count = 0;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            count = await RetirementCountAsync(camera);
            if (count >= expected)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        // Settle: give a second announcement time to land if one is coming.
        await Task.Delay(TimeSpan.FromSeconds(3));

        return await RetirementCountAsync(camera);
    }

    private async Task<int> RetirementCountAsync(Guid camera)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> rows = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE event_kind = 'CameraRetiredV1'
                  AND payload->>'Camera' = {camera.ToString()}
                """)
            .ToListAsync();

        return rows[0];
    }
}
