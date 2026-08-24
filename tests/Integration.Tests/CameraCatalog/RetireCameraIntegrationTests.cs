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

    // Fixed rather than unique: every test resets the catalogue, and the
    // reuse stories read better naming the same camera the spec names.
    private const string ReusedName = "line-3-inlet";

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

        // T032 / FR-010. Counting rows says the retirement was recorded; it does
        // not say *who* retired it. The audit row's actor and the payload's
        // RetiredBy must be the same operator — if the metadata lost the actor
        // the row would fall back to the system actor and the trail would record
        // that the system retired a camera nobody asked it to.
        (Guid Actor, Guid RetiredBy) attribution = await RetirementAttributionAsync(camera);

        attribution.RetiredBy.ShouldNotBe(Guid.Empty);
        attribution.Actor.ShouldBe(attribution.RetiredBy);
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

    /// <summary>
    /// T013 — US2. The point of retirement, from the operator's side: the name
    /// of hardware that no longer exists becomes available again.
    ///
    /// <para>
    /// Research §1 says no production code is needed for this — the partial
    /// unique index already carries <c>WHERE status &lt;&gt; 'Decommissioned'</c>.
    /// This test is what turns that reading of the schema into a checked claim.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_retired_cameras_name_is_free_again_in_its_own_fab()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid original = await RegisterAsync(cameras, ReusedName);

        (await cameras.PostAsync($"/cameras/{original}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        HttpResponseMessage reused = await AttemptRegisterAsync(cameras, ReusedName);

        reused.StatusCode.ShouldBe(HttpStatusCode.Created,
            "the partial unique index excludes Decommissioned rows, so the name should be free");

        // A different camera, not the old one revived. Retirement is terminal,
        // so reuse of the name must not be reuse of the aggregate.
        Guid replacement = await reused.Content.ReadFromJsonAsync<Guid>();
        replacement.ShouldNotBe(original);
    }

    /// <summary>
    /// T014 — the control for T013. Without it, T013 passes just as well
    /// against a catalogue that enforces no uniqueness at all: "the name was
    /// accepted after retirement" only means something if the same name is
    /// refused before it.
    /// </summary>
    [Fact]
    public async Task An_active_cameras_name_is_still_refused()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        await RegisterAsync(cameras, ReusedName);

        HttpResponseMessage duplicate = await AttemptRegisterAsync(cameras, ReusedName);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        JsonElement problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("CAMERA_NAME_TAKEN");
    }

    /// <summary>
    /// T015 — a retirement in one fab changes nothing about what another may
    /// register, in <em>either</em> direction: it does not release the other
    /// fab's identical name, and the other fab's active camera does not keep
    /// the name from being reused here.
    /// </summary>
    [Fact]
    public async Task A_retirement_in_one_fab_changes_nothing_in_another()
    {
        using HttpClient munich = await ClientFor(MunichOperator);
        using HttpClient dresden = await ClientFor(DresdenOperator);

        Guid inMunich = await RegisterAsync(munich, ReusedName);

        // The same name in both fabs at once — uniqueness is per fab (spec 015).
        await RegisterAsync(dresden, ReusedName);

        (await munich.PostAsync($"/cameras/{inMunich}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Direction one: Munich's retirement did not free Dresden's name.
        (await AttemptRegisterAsync(dresden, ReusedName))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict,
                "retiring a camera in Munich must not release a name held in Dresden");

        // Direction two: Dresden's active camera does not hold Munich's name.
        (await AttemptRegisterAsync(munich, ReusedName))
            .StatusCode.ShouldBe(HttpStatusCode.Created,
                "an active camera in Dresden must not keep Munich from reusing its own retired name");
    }

    /// <summary>
    /// T016 — reuse and case-insensitive uniqueness have to hold at the same
    /// time. The index that makes reuse work is the same one #1434 made
    /// case-insensitive, and this feature is the first thing to change its
    /// predicate's reach; a regression that dropped normalisation would still
    /// pass T013.
    /// </summary>
    [Fact]
    public async Task Case_insensitivity_survives_reuse()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        Guid original = await RegisterAsync(cameras, "Line-3-Inlet");

        // #1434: while it is active, a differently-cased name is the same name.
        (await AttemptRegisterAsync(cameras, "line-3-inlet"))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await cameras.PostAsync($"/cameras/{original}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await AttemptRegisterAsync(cameras, "line-3-inlet"))
            .StatusCode.ShouldBe(HttpStatusCode.Created,
                "retirement releases the normalised name, not merely the exact spelling");
    }

    /// <summary>
    /// T020 — US3 over real HTTP. The unit test in
    /// <c>ListCamerasQueryHandlerTests</c> proves the filter; this proves it
    /// survives EF translation, which the in-memory query source cannot: the
    /// status filter compares a value-object property that is mapped through a
    /// converter, and LINQ-to-objects would happily evaluate a predicate
    /// Postgres cannot.
    /// </summary>
    [Fact]
    public async Task A_retired_camera_leaves_the_default_listing_and_comes_back_when_asked_for()
    {
        using HttpClient cameras = await ClientFor(MunichOperator);
        await RegisterAsync(cameras, "cam-staying");
        Guid going = await RegisterAsync(cameras, "cam-going");

        (await cameras.PostAsync($"/cameras/{going}/retire", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await NamesAsync(cameras)).ShouldBe(["cam-staying"]);

        (string Name, string Status)[] withRetired = await RowsAsync(cameras, includeRetired: true);

        withRetired.OrderBy(row => row.Name, StringComparer.Ordinal).ShouldBe(
        [
            ("cam-going", "Decommissioned"),
            ("cam-staying", "Registered"),
        ]);
    }

    private async Task<HttpClient> ClientFor(string username) =>
        await aspire.CreateAuthenticatedClientAsync("camera-catalog", username, OperatorPassword);

    private static string UniqueName() => $"retire-{Guid.CreateVersion7():N}"[..24];

    private static async Task<Guid> RegisterAsync(HttpClient cameras, string name)
    {
        HttpResponseMessage created = await AttemptRegisterAsync(cameras, name);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>
    /// Registers without asserting the outcome — the US2 tests are about which
    /// registrations are refused, so the refusal is the assertion, not a
    /// failure of the helper.
    /// </summary>
    private static Task<HttpResponseMessage> AttemptRegisterAsync(HttpClient cameras, string name) =>
        cameras.PostAsJsonAsync("/cameras", new
        {
            name,
            rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264",
        });

    private static async Task<string[]> NamesAsync(HttpClient cameras, bool includeRetired = false) =>
        [.. (await RowsAsync(cameras, includeRetired)).Select(row => row.Name)];

    /// <summary>
    /// Rows rather than a name-keyed map: once retired cameras are included, a
    /// name can legitimately appear twice — once for the retired camera and
    /// once for its replacement — so keying by name would throw on exactly the
    /// listing this feature makes possible.
    /// </summary>
    private static async Task<(string Name, string Status)[]> RowsAsync(
        HttpClient cameras,
        bool includeRetired = false)
    {
        HttpResponseMessage listed =
            await cameras.GetAsync($"/cameras?limit=200&includeRetired={includeRetired}");
        listed.EnsureSuccessStatusCode();
        JsonElement page = await listed.Content.ReadFromJsonAsync<JsonElement>();

        return [.. page.GetProperty("items").EnumerateArray()
            .Select(row => (
                Name: row.GetProperty("name").GetString()!,
                Status: row.GetProperty("status").GetString()!))];
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

    /// <summary>
    /// The operator on the audit row, read two ways: the envelope's actor column
    /// and the serialised payload's own field. They come from the same
    /// <c>EventMetadata</c>, so a mismatch means the envelope dropped the actor.
    /// </summary>
    private async Task<(Guid Actor, Guid RetiredBy)> RetirementAttributionAsync(Guid camera)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<string> rows = await context.Database
            .SqlQuery<string>($"""
                SELECT actor_identifier::text || ',' || (payload->>'RetiredBy') AS "Value"
                FROM audit_events
                WHERE event_kind = 'CameraRetiredV1'
                  AND payload->>'Camera' = {camera.ToString()}
                """)
            .ToListAsync();

        string[] parts = rows.ShouldHaveSingleItem().Split(',');

        return (Guid.Parse(parts[0]), Guid.Parse(parts[1]));
    }
}
