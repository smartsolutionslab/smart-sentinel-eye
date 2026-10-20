using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using Xunit.Abstractions;

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
    private const string MunichAdminPassword = SeededCredentials.AdminMunich;
    private const string BerlinOperatorUsername = "op-berlin@berlin.test";
    private const string BerlinOperatorPassword = SeededCredentials.OpBerlin;
    private const string HealthTransitionEventKind = "StreamHealthChangedV1";
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
        var arranged = await ArrangedAsync();

        arranged.NullFabRow.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);
        arranged.NullFabRow.GetProperty("resourceKind").GetString().ShouldBe("stream");
        arranged.NullFabRow.GetProperty("resourceIdentifier").GetString().ShouldBe(arranged.Camera.ToString());
        arranged.NullFabRow.GetProperty("payload").GetString()!.ShouldContain(arranged.Camera.ToString());

        output.WriteLine($"SC-1 observed null-fab row: {arranged.NullFabRow}");
    }

    [Fact]
    public async Task The_same_cameras_earlier_transition_recorded_its_fab()
    {
        var arranged = await ArrangedAsync();

        arranged.MunichRow.GetProperty("fab").GetString().ShouldBe("munich");
        arranged.NullFabRow.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);

        output.WriteLine(
            $"SC-2 observed pair: earlier row fab={arranged.MunichRow.GetProperty("fab")}, "
            + $"later row fab={arranged.NullFabRow.GetProperty("fab")}");
    }

    // Spec 306 (#2540) SC-1/SC-2 — the marker that distinguishes "fab-owned
    // but unresolved" from "genuinely fab-neutral" (see also
    // NeutralFabRetentionRowIntegrationTests, which pins the contrasting
    // NotApplicable case on the same null fab: null).
    [Fact]
    public async Task The_null_fab_health_row_is_marked_unresolved()
    {
        var arranged = await ArrangedAsync();

        arranged.NullFabRow.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);
        arranged.NullFabRow.GetProperty("fabAttribution").GetString().ShouldBe("Unresolved",
            "SC-1 (spec 306): StreamHealthChangedV1 is fab-owned, so a null fab here means the fab "
            + "failed to resolve, not that the row has no fab dimension");
    }

    [Fact]
    public async Task The_same_cameras_earlier_row_is_marked_resolved()
    {
        var arranged = await ArrangedAsync();

        arranged.MunichRow.GetProperty("fab").GetString().ShouldBe("munich");
        arranged.MunichRow.GetProperty("fabAttribution").GetString().ShouldBe("Resolved",
            "SC-2 (spec 306) control: the earlier, fab-carrying row is Resolved, proving the marker "
            + "isn't a constant across every row this arrangement produces");
    }

    [Fact]
    public async Task The_fab_scoped_timeline_does_not_return_the_unresolved_row()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();
        var munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using var munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        var response = await munich.GetAsync(
            $"/audit/stream/{arranged.Camera}?fabId=munich&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var identifiers = await AuditIdentifiersAsync(response);
        identifiers.ShouldNotContain(nullFabAuditIdentifier,
            "SC-3 (spec 396): an Unresolved row belongs to some fab nobody resolved, so not even the "
            + "operator of the fab the camera was registered in may read it from the timeline");
        identifiers.ShouldContain(munichAuditIdentifier,
            "the Resolved munich row must still be present, or an empty page would pass for the wrong reason");
    }

    [Fact]
    public async Task The_fab_scoped_search_does_not_return_the_null_fab_row()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();
        var munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using var munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        var response = await munich.GetAsync(
            "/audit?fabId=munich&eventKind=StreamHealthChangedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var identifiers = await AuditIdentifiersAsync(response);
        identifiers.ShouldNotContain(nullFabAuditIdentifier,
            "SC-4 / F3: the explicitly-fab-scoped search must exclude the null-fab row");
        identifiers.ShouldContain(munichAuditIdentifier,
            "the munich row must still be present here, or an empty page would pass for the wrong reason");
    }

    /// <summary>
    /// Spec 396 (#2927), SC-1 / I-2. Replaces the spec 306 FR-007 pin
    /// <c>An_unscoped_search_returns_the_null_fab_row_to_another_fabs_operator</c>,
    /// which recorded the exposure #2540 deliberately kept. The owner decision on
    /// #2927 reverses that: a fab-scoped caller (a berlin operator holds a usable fab
    /// claim) no longer receives an <c>Unresolved</c> row from the search.
    /// </summary>
    [Fact]
    public async Task A_search_without_a_fabId_does_not_return_the_unresolved_row_to_another_fabs_operator()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using var berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        var response = await berlin.GetAsync(
            "/audit?eventKind=StreamHealthChangedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        (await AuditIdentifiersAsync(response)).ShouldNotContain(nullFabAuditIdentifier,
            "SC-1 (spec 396): an Unresolved row must not reach an operator of a different fab");

        var control = await berlin.GetAsync(
            $"/audit?eventKind=CameraRegisteredV1&resourceIdentifier={arranged.BerlinCamera}&pageSize=200");
        control.StatusCode.ShouldBe(HttpStatusCode.OK, await control.Content.ReadAsStringAsync());
        var controlRows = await control.Content.ReadFromJsonAsync<JsonElement>();
        controlRows.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("resourceIdentifier").GetString())
            .ShouldContain(arranged.BerlinCamera.ToString(),
                "the berlin operator must still read its own fab's rows, or the absence above passes on a broken endpoint");
    }

    /// <summary>
    /// Spec 396 (#2927), SC-2 / I-3. The row's own fab's operator is fab-scoped too, and
    /// the row names no fab, so it is nobody's to show (the cost option A accepted).
    /// </summary>
    [Fact]
    public async Task A_search_without_a_fabId_does_not_return_the_unresolved_row_to_its_own_fabs_operator()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();
        var munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using var munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        var response = await munich.GetAsync(
            "/audit?eventKind=StreamHealthChangedV1&pageSize=200");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var identifiers = await AuditIdentifiersAsync(response);
        identifiers.ShouldNotContain(nullFabAuditIdentifier,
            "SC-2 (spec 396): not even the operator of the camera's own fab may read the Unresolved row");
        identifiers.ShouldContain(munichAuditIdentifier,
            "the camera's earlier Resolved row must still be present, or an empty page would pass for the wrong reason");
    }

    /// <summary>
    /// Spec 396 (#2927), SC-9 / I-4. Replaces the spec 306 FR-007 pin
    /// <c>Get_single_returns_the_null_fab_row_to_another_fabs_operator</c>. A hidden
    /// row answers <c>404 AUDIT_EVENT_NOT_FOUND</c>, indistinguishable from an
    /// identifier that never existed: a 403 would confirm the row exists.
    /// </summary>
    [Fact]
    public async Task Get_single_answers_not_found_for_the_unresolved_row_to_another_fabs_operator()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using var berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        await AssertNotFoundIndistinguishableFromUnknownAsync(berlin, nullFabAuditIdentifier);
    }

    /// <summary>Spec 396 (#2927), SC-9 / I-5: the same for the row's own fab's operator.</summary>
    [Fact]
    public async Task Get_single_answers_not_found_for_the_unresolved_row_to_its_own_fabs_operator()
    {
        var arranged = await ArrangedAsync();
        var nullFabAuditIdentifier = arranged.NullFabRow.GetProperty("auditIdentifier").GetGuid();

        using var munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        await AssertNotFoundIndistinguishableFromUnknownAsync(munich, nullFabAuditIdentifier);
    }

    [Fact]
    public async Task Get_single_answers_not_found_for_the_same_cameras_munich_row_to_that_operator()
    {
        var arranged = await ArrangedAsync();
        var munichAuditIdentifier = arranged.MunichRow.GetProperty("auditIdentifier").GetGuid();

        using var berlin = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", BerlinOperatorUsername, BerlinOperatorPassword);

        await AssertNotFoundIndistinguishableFromUnknownAsync(berlin, munichAuditIdentifier);
    }

    [Fact]
    public async Task A_cross_fab_timeline_is_still_refused()
    {
        var arranged = await ArrangedAsync();

        using var munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        var response = await munich.GetAsync($"/audit/stream/{arranged.Camera}?fabId=berlin");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    [Fact]
    public async Task The_pivot_identifier_is_not_listable_by_another_fabs_operator()
    {
        var arranged = await ArrangedAsync();

        using var berlin = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", BerlinOperatorUsername, BerlinOperatorPassword);

        var response = await berlin.GetAsync("/cameras?limit=200&includeRetired=true");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
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

        using var munichCameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MunichAdminUsername, MunichAdminPassword);
        using var munichStreams = await aspire.CreateAuthenticatedClientAsync(
            "stream-distribution", MunichAdminUsername, MunichAdminPassword);
        using var munichAudit = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        var camera = await RegisterCameraAsync(
            munichCameras, $"Cam-NullFab-{Guid.NewGuid():N}", AspireFixture.RtspTestSourceUrl);

        await WaitForStreamStateAsync(munichStreams, camera, "Healthy", SettleTimeout);

        var munichRow = await PollForTimelineRowAsync(
            munichAudit, camera,
            rows => rows.Length > 0 ? rows[0] : null,
            "the first (munich) StreamHealthChangedV1");
        AssertIsHealthTransition(munichRow, "Healthy");

        await BlankTheFabAsync(camera);

        var path = MediaMtxPath.For(CameraIdentifier.From(camera)).Value;
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

        // Read from the audit store, not over HTTP: spec 396 hides this row from
        // every fab-scoped caller, and no seeded principal is unscoped.
        var nullFabRow = await PollForStoredNullFabRowAsync(camera, munichRow.GetProperty("auditIdentifier").GetGuid());
        // The poll picks the earliest non-munich health transition by time, not
        // by ToState — the restore-to-reachable in the finally above can
        // itself provoke a THIRD (Degraded -> Healthy) row before this poll
        // runs. Asserting the transition here turns a silent wrong-row pick
        // into an immediate, attributable failure instead of a confusing
        // downstream one.
        AssertIsHealthTransition(nullFabRow, "Degraded");

        using var berlinCameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", BerlinOperatorUsername, BerlinOperatorPassword);
        var berlinCamera = await RegisterCameraAsync(
            berlinCameras, $"Cam-BerlinControl-{Guid.NewGuid():N}",
            $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264");

        output.WriteLine($"Arrangement complete. camera={camera}, munichRow={munichRow}, nullFabRow={nullFabRow}");

        return new Arrangement(camera, munichRow, nullFabRow, berlinCamera);
    }

    private static async Task<Guid> RegisterCameraAsync(HttpClient client, string name, string rtspUrl)
    {
        var response = await client.PostAsJsonAsync("/cameras", new { name, rtspUrl });
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
        await using var context = await aspire.CreateStreamDistributionDbContextAsync();
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
        var deadline = DateTime.UtcNow + timeout;
        var lastObserved = "<no row found>";

        while (DateTime.UtcNow < deadline)
        {
            await using var context = await aspire.CreateStreamDistributionDbContextAsync();
            var stream = await context.Streams.AsNoTracking()
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
        var deadline = DateTime.UtcNow + timeout;
        var lastObserved = "<no response yet>";

        while (DateTime.UtcNow < deadline)
        {
            var response = await streamClient.GetAsync($"/streams?cameraIdentifiers={camera}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var items = await response.Content.ReadFromJsonAsync<JsonElement>();
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
    /// Polls the audit store for the earliest non-munich stream health transition of
    /// <paramref name="camera"/> and projects it into the JSON shape the facts read
    /// from the HTTP responses (the same property names; <c>AuditRowMapper</c> is
    /// <c>internal</c>, so the projection is test-local). Needed since spec 396: the
    /// munich timeline no longer returns this <c>Unresolved</c> row, and no seeded HTTP
    /// principal is unscoped, so nothing over HTTP can fetch its identifier.
    /// </summary>
    private async Task<JsonElement> PollForStoredNullFabRowAsync(Guid camera, Guid munichAuditIdentifier)
    {
        var deadline = DateTime.UtcNow + AuditPollTimeout;
        var resource = ResourceIdentifier.From(camera.ToString());
        while (DateTime.UtcNow < deadline)
        {
            await using var context = await aspire.CreateAuditObservabilityDbContextAsync();
            var rows = await context.AuditEvents.AsNoTracking()
                .Where(audit => audit.ResourceIdentifier == resource)
                .ToListAsync();

            // The resource identifier is the camera's, so its CameraRegisteredV1 row
            // matches too; select the health transitions by kind, then take the
            // earliest one that is not the munich row. The fab is not a filter, so
            // the fab and attribution assertions can still fail.
            var healthTransitions = rows
                .Where(audit => string.Equals(audit.EventKind.Value, HealthTransitionEventKind, StringComparison.Ordinal))
                .Where(audit => audit.Id.Value != munichAuditIdentifier)
                .OrderBy(audit => audit.OccurredAt.Value);

            if (healthTransitions.FirstOrDefault() is { } stored)
            {
                return JsonSerializer.SerializeToElement(new
                {
                    auditIdentifier = stored.Id.Value,
                    fab = stored.Fab?.Value,
                    fabAttribution = stored.FabAttribution.ToString(),
                    eventKind = stored.EventKind.Value,
                    resourceKind = stored.ResourceKind?.Value,
                    resourceIdentifier = stored.ResourceIdentifier?.Value,
                    payload = stored.Payload.Content.Value,
                });
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No second stream health audit row for camera {camera} appeared in the audit store within {AuditPollTimeout.TotalSeconds:F0}s.");
    }

    /// <summary>
    /// Spec 396 (#2927), SC-9: a hidden row and an identifier that never existed must
    /// be the same answer, or the 404 is an existence oracle. Status and title are
    /// compared as they are; the detail embeds the requested identifier, so it is
    /// compared after substituting each one.
    /// </summary>
    private static async Task AssertNotFoundIndistinguishableFromUnknownAsync(HttpClient client, Guid hidden)
    {
        var unknown = Guid.CreateVersion7();

        var hiddenResponse = await client.GetAsync($"/audit/{hidden}");
        var unknownResponse = await client.GetAsync($"/audit/{unknown}");

        hiddenResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound, await hiddenResponse.Content.ReadAsStringAsync());
        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var hiddenProblem = await hiddenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var unknownProblem = await unknownResponse.Content.ReadFromJsonAsync<JsonElement>();

        hiddenProblem.GetProperty("title").GetString().ShouldBe("AUDIT_EVENT_NOT_FOUND");
        hiddenProblem.GetProperty("title").GetString().ShouldBe(unknownProblem.GetProperty("title").GetString());
        hiddenProblem.GetProperty("status").GetInt32().ShouldBe(unknownProblem.GetProperty("status").GetInt32());
        hiddenProblem.GetProperty("detail").GetString()!.Replace(hidden.ToString(), "{id}", StringComparison.Ordinal)
            .ShouldBe(unknownProblem.GetProperty("detail").GetString()!
                .Replace(unknown.ToString(), "{id}", StringComparison.Ordinal));
    }

    /// <summary>
    /// Polls the per-resource timeline for <paramref name="camera"/> — which
    /// admits the fab's own rows (and fab-neutral ones) for a munich-scoped
    /// caller — until <paramref name="select"/> finds the row it is looking
    /// for among the rows currently on the page.
    /// </summary>
    private static async Task<JsonElement> PollForTimelineRowAsync(
        HttpClient audit, Guid camera, Func<JsonElement[], JsonElement?> select, string what)
    {
        var deadline = DateTime.UtcNow + AuditPollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await audit.GetAsync($"/audit/stream/{camera}?fabId=munich&pageSize=200");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
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
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
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
