using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using Xunit.Abstractions;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 217 (#2517), US1 — SC-1 through SC-9. A camera whose stream genuinely
/// has no fab (a manufactured pre-spec-016 row shape, per spec.md G1) is
/// transitioned through the real health-watcher path, and the audit row it
/// produces is read back through all three read surfaces.
///
/// <para>
/// <b>One arrangement, shared by every fact.</b> Registering a camera,
/// reaching <c>Healthy</c>, blanking the fab and reaching <c>Degraded</c>
/// costs roughly 45s and every fact needs the same two rows. xUnit creates a
/// fresh instance of this class per <c>[Fact]</c>, so a plain instance field
/// would rebuild it nine times and — worse — <see cref="InitializeAsync"/>'s
/// resets would wipe the camera and stream a previous fact's arrangement just
/// created. The arrangement is therefore cached in a <c>static</c> field,
/// built exactly once by whichever fact's <see cref="InitializeAsync"/> runs
/// first; every other fact awaits the same completed
/// <see cref="Task{TResult}"/>. Safe without an explicit lock because the
/// <see cref="AspireCollection"/> serializes every test in it — the same
/// guarantee <c>plan.md</c>'s risk table already relies on for two classes
/// not overlapping.
/// </para>
///
/// <para>
/// <b>Order is load-bearing.</b> The camera must reach <c>Healthy</c> before
/// its fab is blanked: registering at an unreachable address from the start
/// reaches <c>Degraded</c> within ~2 sweeps, before the fab could be cleared,
/// and there would be no second transition to observe (plan.md, Test design —
/// US1).
/// </para>
///
/// <para>
/// <b>The audit database is never reset</b> — unlike the other three
/// resources — because SC-5 and SC-6 read rows this class wrote earlier in
/// the run; a reset between arrange and assert would make an empty page
/// indistinguishable from a correct exclusion.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class UnresolvedFabAuditRowIntegrationTests(AspireFixture aspire, ITestOutputHelper output) : IAsyncLifetime
{
    private const string MunichAdminUsername = "admin@munich.test";
    private const string MunichAdminPassword = "Admin1234";
    private const string BerlinOperatorUsername = "op-berlin@berlin.test";
    private const string BerlinOperatorPassword = "Operator1234";
    private const string UnreachableRtspUrl = "rtsp://10.0.6.1/h264";

    /// <summary>Matches <c>StreamHealthTransitionTests.SettleTimeout</c>.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Matches <c>StreamHealthTransitionTests.TransitionTimeout</c>.</summary>
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuditPollTimeout = TimeSpan.FromSeconds(20);

    private static Task<Arrangement>? cachedArrangement;

    public Task InitializeAsync()
    {
        cachedArrangement ??= ArrangeOnceAsync();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_stream_with_no_fab_records_a_null_fab_on_its_health_audit_row()
    {
        Arrangement arranged = await ArrangedAsync();

        arranged.NullFabRow.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);
        arranged.NullFabRow.GetProperty("resourceKind").GetString().ShouldBe("stream");
        arranged.NullFabRow.GetProperty("resourceIdentifier").GetString().ShouldBe(arranged.Camera.ToString());
        arranged.NullFabRow.GetProperty("payload").GetString()!.ShouldContain(arranged.Camera.ToString());

        output.WriteLine($"SC-1 observed null-fab row: {arranged.NullFabRow}");
    }

    [Fact]
    public async Task The_same_cameras_earlier_transition_recorded_its_fab()
    {
        Arrangement arranged = await ArrangedAsync();

        arranged.MunichRow.GetProperty("fab").GetString().ShouldBe("munich");
        arranged.NullFabRow.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);

        output.WriteLine(
            $"SC-2 observed pair: earlier row fab={arranged.MunichRow.GetProperty("fab")}, "
            + $"later row fab={arranged.NullFabRow.GetProperty("fab")}");
    }

    [Fact]
    public async Task The_fab_scoped_timeline_returns_the_null_fab_row()
    {
        Arrangement arranged = await ArrangedAsync();
        Guid nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using HttpClient munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        HttpResponseMessage response = await munich.GetAsync(
            $"/audit/stream/{arranged.Camera}?fabId=munich&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        Guid[] identifiers = await AuditIdentifiersAsync(response);
        identifiers.ShouldContain(nullFabAuditIdentifier,
            "SC-3 (#2506): the widened per-resource timeline must return the null-fab row to its own fab's operator");
    }

    [Fact]
    public async Task The_fab_scoped_search_does_not_return_the_null_fab_row()
    {
        Arrangement arranged = await ArrangedAsync();
        Guid nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();
        Guid munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using HttpClient munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        HttpResponseMessage response = await munich.GetAsync(
            "/audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        Guid[] identifiers = await AuditIdentifiersAsync(response);
        identifiers.ShouldNotContain(nullFabAuditIdentifier,
            "SC-4 / F3: the explicitly-fab-scoped search must exclude the null-fab row");
        identifiers.ShouldContain(munichAuditIdentifier,
            "the munich row must still be present here, or an empty page would pass for the wrong reason");
    }

    /// <summary>
    /// SC-5. <b>This records the current, undecided exposure — it is not a
    /// requirement that this stay true.</b> #2517 asks a human to decide
    /// whether a fab-owned resource may legitimately record a null fab; this
    /// fact is one of the two read paths that make the answer "yes, and
    /// readable by anyone" today (see also
    /// <see cref="Get_single_returns_the_null_fab_row_to_another_fabs_operator"/>).
    /// Whichever way #2540 resolves that question, a fix that excludes a
    /// null-fab row from the unscoped search turns this fact red. <b>That red is the fix landing, not a
    /// regression</b> — per ADR-0144 this delivery cannot weaken the
    /// assertion to pre-empt it, so update this comment (not the assertion)
    /// to point at the closed decision when that day comes.
    /// </summary>
    [Fact]
    public async Task An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator()
    {
        Arrangement arranged = await ArrangedAsync();
        Guid nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        HttpResponseMessage response = await berlin.GetAsync(
            "/audit?eventKind=StreamHealthChangedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement[] rows = [.. page.GetProperty("rows").EnumerateArray()];
        Guid[] identifiers = [.. rows.Select(row => row.GetProperty("auditIdentifier").GetGuid())];

        identifiers.ShouldContain(nullFabAuditIdentifier,
            "SC-5: the unscoped search must still return a cross-fab row to an operator of a different fab (#1300)");

        // What the foreign-fab operator actually receives, not merely that a
        // row exists — payload severity is what a human deciding the policy
        // question needs to weigh.
        rows.Single(row => row.GetProperty("auditIdentifier").GetGuid() == nullFabAuditIdentifier)
            .GetProperty("payload").GetString()!.ShouldContain(arranged.Camera.ToString(),
                customMessage: "the disclosed row's payload must name the munich camera, or 'a row was returned' understates what leaked");
    }

    /// <summary>
    /// SC-6 / F0. <b>This records the current, undecided exposure — it is not
    /// a requirement that this stay true.</b> <c>GetSingle</c> skips
    /// <c>IFabAuthorizationGuard</c> entirely when a row's fab is null
    /// (<c>AuditEndpoints.cs</c>), so today any <c>sse.audit.read</c> holder
    /// who has the audit identifier reads it, whatever fab the underlying
    /// resource belongs to. #2517 asks a human to decide whether that
    /// conflation is acceptable; whichever way #2540 resolves it, a fix that
    /// adds a guard here (or resolves the fab before returning) turns this
    /// fact red. <b>That red is the fix
    /// landing, not a regression</b> — per ADR-0144 this delivery cannot
    /// weaken the assertion to pre-empt it, so update this comment (not the
    /// assertion) to point at the closed decision when that day comes.
    /// </summary>
    [Fact]
    public async Task Get_single_returns_the_null_fab_row_to_another_fabs_operator()
    {
        Arrangement arranged = await ArrangedAsync();
        Guid nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        HttpResponseMessage response = await berlin.GetAsync($"/audit/{nullFabAuditIdentifier}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement row = await response.Content.ReadFromJsonAsync<JsonElement>();
        row.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null,
            "SC-6 / F0: GetSingle skips the fab guard entirely when the row's fab is null");
        // As above: what the foreign-fab operator actually receives.
        row.GetProperty("payload").GetString()!.ShouldContain(arranged.Camera.ToString(),
            customMessage: "the disclosed row's payload must name the munich camera, or 'a row was returned' understates what leaked");
    }

    [Fact]
    public async Task Get_single_refuses_the_same_cameras_munich_row_to_that_operator()
    {
        Arrangement arranged = await ArrangedAsync();
        Guid munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        HttpResponseMessage response = await berlin.GetAsync($"/audit/{munichAuditIdentifier}");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task A_cross_fab_timeline_is_still_refused()
    {
        Arrangement arranged = await ArrangedAsync();

        using HttpClient munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        HttpResponseMessage response = await munich.GetAsync($"/audit/stream/{arranged.Camera}?fabId=berlin");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task The_pivot_identifier_is_not_listable_by_another_fabs_operator()
    {
        Arrangement arranged = await ArrangedAsync();

        using HttpClient berlin = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", BerlinOperatorUsername, BerlinOperatorPassword);

        HttpResponseMessage response = await berlin.GetAsync("/cameras?limit=200&includeRetired=true");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Guid[] identifiers = [.. page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("cameraIdentifier").GetGuid())];

        identifiers.ShouldNotContain(arranged.Camera,
            "SC-9 / F2: a munich camera must not be listable by a berlin-only operator's own catalogue read");
        identifiers.ShouldContain(arranged.BerlinCamera,
            "the listing must include at least the control camera, or the absence above passes on a broken endpoint");
    }

    /// <summary>Awaits the shared, once-built arrangement.</summary>
    private static Task<Arrangement> ArrangedAsync() => cachedArrangement!;

    /// <summary>
    /// Builds the one arrangement every fact in this class reads. Runs the
    /// three resets <c>StreamHealthTransitionTests</c> runs — deliberately not
    /// the audit database — then drives a camera through a real Healthy
    /// transition, blanks its stream's fab by raw SQL (the shape
    /// <c>StreamFabAttributionIntegrationTests.BlankTheFabAsync</c> uses,
    /// because <c>Stream.Provision</c> requires a fab and there is no setter),
    /// and drives a second, real Degraded transition so the health watcher
    /// publishes with a null fab.
    /// </summary>
    private async Task<Arrangement> ArrangeOnceAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
        await aspire.ResetCameraCatalogAsync();

        using HttpClient munichCameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MunichAdminUsername, MunichAdminPassword);
        using HttpClient munichStreams = await aspire.CreateAuthenticatedClientAsync(
            "stream-distribution", MunichAdminUsername, MunichAdminPassword);
        using HttpClient munichAudit = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        Guid camera = await RegisterCameraAsync(
            munichCameras, $"Cam-NullFab-{Guid.NewGuid():N}", AspireFixture.RtspTestSourceUrl);

        await WaitForStreamStateAsync(munichStreams, camera, "Healthy", SettleTimeout);

        JsonElement munichRow = await PollForTimelineRowAsync(
            munichAudit, camera,
            rows => rows.Length > 0 ? rows[0] : null,
            "the first (munich) StreamHealthChangedV1");
        AssertIsHealthTransition(munichRow, "Healthy");

        await BlankTheFabAsync(camera);

        string path = MediaMtxPath.For(CameraIdentifier.From(camera)).Value;
        try
        {
            await aspire.RepointMediaMtxPathAsync(path, UnreachableRtspUrl);

            // Not WaitForStreamStateAsync (HTTP) from here on: FR-009
            // (StreamFabAttributionIntegrationTests.A_stream_with_no_fab_is_returned_to_nobody)
            // makes a null-fab stream invisible on every read path, including
            // this one — GET /streams would poll an empty page for the rest of
            // the timeout and time out for the wrong reason. The store itself
            // has no such guard.
            await WaitForStreamStateInStoreAsync(camera, StreamState.Degraded, TransitionTimeout);
        }
        finally
        {
            try
            {
                await aspire.RepointMediaMtxPathAsync(path, AspireFixture.RtspTestSourceUrl);
            }
            catch (HttpRequestException restoreFailed)
            {
                output.WriteLine($"Restoring '{path}' to the fixture source failed: {restoreFailed.Message}");
            }
        }

        Guid munichAuditIdentifier = munichRow.GetProperty("auditIdentifier").GetGuid();
        JsonElement nullFabRow = await PollForTimelineRowAsync(
            munichAudit, camera,
            rows => rows.FirstOrDefault(row => row.GetProperty("auditIdentifier").GetGuid() != munichAuditIdentifier)
                is { ValueKind: not JsonValueKind.Undefined } found ? found : null,
            "the second (null-fab) StreamHealthChangedV1");
        // The selector above picks the first non-munich row by position, not
        // by kind or transition — cheap today because there is only one V1 in
        // the stream namespace, but the restore-to-reachable in the finally
        // above can itself provoke a THIRD (Degraded -> Healthy) row before
        // this poll runs. Asserting the transition here turns a silent
        // wrong-row pick into an immediate, attributable failure instead of a
        // confusing downstream one.
        AssertIsHealthTransition(nullFabRow, "Degraded");

        using HttpClient berlinCameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", BerlinOperatorUsername, BerlinOperatorPassword);
        Guid berlinCamera = await RegisterCameraAsync(
            berlinCameras, $"Cam-BerlinControl-{Guid.NewGuid():N}",
            $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264");

        output.WriteLine($"Arrangement complete. camera={camera}, munichRow={munichRow}, nullFabRow={nullFabRow}");

        return new Arrangement(camera, munichRow, nullFabRow, berlinCamera);
    }

    private static async Task<Guid> RegisterCameraAsync(HttpClient client, string name, string rtspUrl)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/cameras", new { name, rtspUrl });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>
    /// Recreates a row that predates the fab column. Written through SQL
    /// rather than the aggregate: <c>Provision</c> requires a fab and there is
    /// no setter, and a raw write bypasses the EF concurrency token
    /// <c>StreamHealthWatcher</c> is contending for every 2 s.
    /// </summary>
    private async Task BlankTheFabAsync(Guid camera)
    {
        await using StreamDistributionDbContext context = await aspire.CreateStreamDistributionDbContextAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE streams SET fab = NULL WHERE camera_id = {camera}");
    }

    /// <summary>
    /// Polls the store directly rather than <c>GET /streams</c>: once
    /// <see cref="BlankTheFabAsync"/> has run, the stream's fab is null and
    /// every HTTP read path hides a null-fab stream from every caller
    /// (FR-009) — polling the HTTP endpoint here would see an empty page for
    /// the whole timeout and fail for a different reason than the one this
    /// arrangement is trying to observe.
    /// </summary>
    private async Task<StreamState> WaitForStreamStateInStoreAsync(Guid camera, StreamState expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        string lastObserved = "<no row found>";

        while (DateTime.UtcNow < deadline)
        {
            await using StreamDistributionDbContext context = await aspire.CreateStreamDistributionDbContextAsync();
            StreamAggregate? stream = await context.Streams.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Camera == CameraIdentifier.From(camera));

            if (stream is not null)
            {
                lastObserved = stream.State.ToString();
                if (stream.State == expected)
                {
                    return stream.State;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not reach '{expected}' within {timeout.TotalSeconds:F0}s, "
            + $"read directly from the store. Last observed state: '{lastObserved}'.");
    }

    private static async Task<string> WaitForStreamStateAsync(
        HttpClient streamClient, Guid camera, string expectedState, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        string lastObserved = "<no response yet>";

        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await streamClient.GetAsync($"/streams?cameraIdentifiers={camera}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JsonElement items = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (items.GetArrayLength() == 1)
                {
                    lastObserved = items[0].GetProperty("state").GetString() ?? "<null state>";
                    if (string.Equals(lastObserved, expectedState, StringComparison.Ordinal))
                    {
                        return lastObserved;
                    }
                }
                else
                {
                    lastObserved = $"<{items.GetArrayLength()} streams for the camera>";
                }
            }
            else
            {
                lastObserved = $"<HTTP {(int)response.StatusCode}>";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException(
            $"Stream for camera {camera} did not reach '{expectedState}' within {timeout.TotalSeconds:F0}s. "
            + $"Last observed state: '{lastObserved}'.");
    }

    /// <summary>
    /// Polls the per-resource timeline for <paramref name="camera"/> — which
    /// admits both a fab-carrying and a null-fab row for a munich-scoped
    /// caller — until <paramref name="select"/> finds the row it is looking
    /// for among the rows currently on the page.
    /// </summary>
    private static async Task<JsonElement> PollForTimelineRowAsync(
        HttpClient audit, Guid camera, Func<JsonElement[], JsonElement?> select, string what)
    {
        DateTime deadline = DateTime.UtcNow + AuditPollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await audit.GetAsync($"/audit/stream/{camera}?fabId=munich&pageSize=200");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            JsonElement[] rows = [.. page.GetProperty("rows").EnumerateArray()];

            if (select(rows) is { } found)
            {
                return found;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No {what} row for camera {camera} appeared within {AuditPollTimeout.TotalSeconds:F0}s.");
    }

    /// <summary>
    /// Guards the two callers that pick a row by position (first row; first
    /// row that isn't the other one) rather than by content: confirms the row
    /// is actually a <c>StreamHealthChangedV1</c> whose <c>ToState</c> matches
    /// what the caller believes it just captured.
    /// </summary>
    private static void AssertIsHealthTransition(JsonElement row, string expectedToState)
    {
        row.GetProperty("eventKind").GetString().ShouldBe("StreamHealthChangedV1");
        ParsePayload(row).GetProperty("ToState").GetString().ShouldBe(expectedToState);
    }

    private static JsonElement ParsePayload(JsonElement row) =>
        JsonDocument.Parse(row.GetProperty("payload").GetString()!).RootElement;

    private static async Task<Guid[]> AuditIdentifiersAsync(HttpResponseMessage response)
    {
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("auditIdentifier").GetGuid())];
    }

    /// <summary>
    /// The two audit rows one camera's health transitions produced, plus a
    /// second, berlin-registered camera that exists solely so SC-9's "absent"
    /// assertion is paired with a "present" one (an empty berlin catalogue
    /// would otherwise make the absence trivially true).
    /// </summary>
    private sealed record Arrangement(Guid Camera, JsonElement MunichRow, JsonElement NullFabRow, Guid BerlinCamera);
}
