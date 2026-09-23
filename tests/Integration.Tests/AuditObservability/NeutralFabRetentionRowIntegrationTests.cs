using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 217 (#2517), US2 — SC-10 through SC-12. The contrast to
/// <see cref="UnresolvedFabAuditRowIntegrationTests"/>: <c>AuditChunkArchivedV1</c>
/// is fab-neutral <em>by construction</em> (spec.md §F1) rather than a
/// fab-owned row whose fab failed to resolve, and this drives a real archive
/// sweep to turn that claim from a source read into an observation.
///
/// <para>
/// Independent of <see cref="UnresolvedFabAuditRowIntegrationTests"/> — a
/// different subject (retention, not stream health), a different producer
/// (<c>AuditRetentionHostedService</c>, not the health watcher), no shared
/// helper, no shared fixture state (plan.md, "Why two new files").
/// </para>
///
/// <para>
/// <b>One arrangement, shared by every fact</b> — same reasoning and the same
/// static-cache shape as the sibling class: xUnit creates a fresh instance
/// per <c>[Fact]</c>, and the arrangement (a back-dated insert plus a real
/// sweep) must run exactly once so every fact reads the same announcement.
/// Safe without an explicit lock because the <see cref="AspireCollection"/>
/// serializes every test in it.
/// </para>
///
/// <para>
/// <b>SC-11's counterfactual is produced, not merely reused.</b> tasks.md
/// describes reading "whatever fab-carrying row the run already has"; this
/// registers one munich camera instead of relying on ambient rows from other
/// test classes, so the class proves what it claims whether it runs alone or
/// as part of the full suite. Registering a camera drives the real
/// <c>CameraRegisteredDomainEventHandler</c> publish path — the "ingestion
/// path in this run" tasks.md's own wording refers to — rather than a raw SQL
/// seed, so it is not the shortcut US2 explicitly rules out for the seeded
/// chunk row itself.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class NeutralFabRetentionRowIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string MunichAdminUsername = "admin@munich.test";
    private const string MunichAdminPassword = "Admin1234";

    /// <summary>
    /// The hypertable's chunk interval is one month;
    /// <c>RetentionRoundtripIntegrationTests</c> owns -200d and -120d. ~400d
    /// keeps this class in a distinct chunk (G3), verified below rather than
    /// assumed.
    /// </summary>
    private const int SeedAgeInDays = 400;

    private static readonly TimeSpan AnnouncementPollTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CameraRowPollTimeout = TimeSpan.FromSeconds(20);

    private static Task<Arrangement>? cachedArrangement;

    [Fact]
    public async Task An_archived_chunks_announcement_records_no_fab()
    {
        Arrangement arranged = await ArrangedAsync();

        arranged.Announcement.GetProperty("fab").ValueKind.ShouldBe(JsonValueKind.Null);

        JsonElement payload = ParsePayload(arranged.Announcement);
        payload.GetProperty("FabId").ValueKind.ShouldBe(JsonValueKind.Null,
            "SC-10 / F1: nothing in the system could set a fab on a chunk announcement — "
            + "the hypertable is time-partitioned only, and the sole publisher passes a literal null");
    }

    [Fact]
    public async Task A_fab_carrying_row_in_the_same_window_is_not_null()
    {
        Arrangement arranged = await ArrangedAsync();

        arranged.FabCarryingRow.GetProperty("fab").GetString().ShouldBe("munich",
            "SC-11, counterfactual: this run's ingestion path can write a fab, "
            + "so the null fab on the chunk announcement is not an artefact of a broken pipeline");
    }

    [Fact]
    public async Task The_neutral_announcement_is_reachable_from_a_fab_scoped_timeline()
    {
        Arrangement arranged = await ArrangedAsync();
        string chunkIdentifier = arranged.Announcement.GetProperty("resourceIdentifier").GetString()!;
        Guid announcementAuditIdentifier = arranged.Announcement.GetProperty("auditIdentifier").GetGuid();

        using HttpClient munich = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        HttpResponseMessage response = await munich.GetAsync($"/audit/event/{chunkIdentifier}?fabId=munich");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Guid[] identifiers = [.. page.GetProperty("rows").EnumerateArray()
            .Select(row => row.GetProperty("auditIdentifier").GetGuid())];

        identifiers.ShouldContain(announcementAuditIdentifier,
            "SC-12: #2506 working as intended on the class it was written for");
    }

    /// <summary>
    /// Awaits the shared, once-built arrangement. <c>ArrangeOnceAsync</c> is an
    /// instance method — it closes over <c>this.aspire</c> — but is only ever
    /// actually invoked by whichever instance's fact reaches this line first;
    /// the null-coalescing assignment to the <c>static</c> field is what makes
    /// every later instance (xUnit creates one per <c>[Fact]</c>) await that
    /// same task instead of building its own. <c>aspire</c> itself is the same
    /// collection fixture in every instance regardless
    /// (<see cref="AspireCollection"/>), so which instance wins the race is
    /// immaterial.
    /// </summary>
    private Task<Arrangement> ArrangedAsync() => cachedArrangement ??= ArrangeOnceAsync();

    private async Task<Arrangement> ArrangeOnceAsync()
    {
        // Captured before anything else: Postgres runs ContainerLifetime.Persistent
        // with a data volume, so audit_events survives across runs, and a prior
        // run's seed at the same ~400d age lands in the same monthly chunk. A
        // match on chunk range alone can return an EARLIER run's announcement on
        // the very first poll iteration, before this run's own sweep has done
        // anything — PollForCoveringAnnouncementAsync's freshness check below is
        // what tells this run's evidence apart from a stale one.
        DateTimeOffset arrangementStartedAt = DateTimeOffset.UtcNow;

        DateTimeOffset seededMoment = DateTimeOffset.UtcNow.AddDays(-SeedAgeInDays);
        await SeedNullFabBackdatedRowAsync(seededMoment);
        await AssertNoChunkCollisionAsync(seededMoment);

        using HttpClient munichAudit = await aspire.CreateAuthenticatedClientAsync(
            "audit-observability", MunichAdminUsername, MunichAdminPassword);

        JsonElement announcement = await PollForCoveringAnnouncementAsync(
            munichAudit, seededMoment, arrangementStartedAt);

        using HttpClient munichCameras = await aspire.CreateAuthenticatedClientAsync(
            "camera-catalog", MunichAdminUsername, MunichAdminPassword);
        Guid camera = await RegisterCameraAsync(munichCameras, $"Cam-NeutralFabControl-{Guid.NewGuid():N}");
        JsonElement fabCarryingRow = await PollForCameraRowAsync(munichAudit, camera);

        output.WriteLine(
            $"Arrangement complete. seededMoment={seededMoment:O}, arrangementStartedAt={arrangementStartedAt:O}, "
            + $"announcement={announcement}, fabCarryingRow={fabCarryingRow}");

        return new Arrangement(announcement, fabCarryingRow);
    }

    private static async Task<Guid> RegisterCameraAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/cameras",
            new { name, rtspUrl = $"rtsp://10.0.5.{Random.Shared.Next(2, 250)}/h264" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<JsonElement> PollForCameraRowAsync(HttpClient audit, Guid camera)
    {
        DateTime deadline = DateTime.UtcNow + CameraRowPollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await audit.GetAsync(
                $"/audit?fabId=munich&eventKind=CameraRegisteredV1&pageSize=50");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            foreach (JsonElement row in page.GetProperty("rows").EnumerateArray())
            {
                if (row.GetProperty("resourceIdentifier").GetString() == camera.ToString())
                {
                    return row;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No CameraRegisteredV1 audit row for camera {camera} appeared within {CameraRowPollTimeout.TotalSeconds:F0}s.");
    }

    /// <summary>
    /// Copies <c>RetentionRoundtripIntegrationTests.SeedBackdatedRowAsync</c>'s
    /// column list verbatim — the <c>::jsonb</c> cast and the
    /// <c>payload_size_bytes</c> value matter — changing only the timestamp and
    /// the event kind, so this seed is never mistaken for the sibling class's.
    /// </summary>
    private async Task SeedNullFabBackdatedRowAsync(DateTimeOffset occurredAt)
    {
        await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();

        string emptyJson = "{}";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_events (
                audit_id, occurred_at, received_at, fab_id, event_kind,
                resource_kind, resource_identifier, actor_identifier,
                actor_username, event_identifier, payload, payload_size_bytes,
                schema_version)
            VALUES (
                {Guid.CreateVersion7()}, {occurredAt}, {DateTimeOffset.UtcNow}, NULL,
                'NullFabRetentionSeedV1', NULL, NULL, {Guid.Empty}, NULL,
                {Guid.CreateVersion7()}, {emptyJson}::jsonb, 2, 1)
            """);
    }

    /// <summary>
    /// G3. The interval is one month, so a naive far-past seed can still land
    /// in the same chunk as <c>RetentionRoundtripIntegrationTests</c>' -200d or
    /// -120d rows. Verified against <c>timescaledb_information.chunks</c>
    /// rather than assumed: a chunk containing both <paramref name="seededMoment"/>
    /// and one of the sibling's seeded moments is a collision.
    /// </summary>
    private async Task AssertNoChunkCollisionAsync(DateTimeOffset seededMoment)
    {
        bool collidesWith200 = await ChunkContainsBothAsync(
            seededMoment, DateTimeOffset.UtcNow.AddDays(-200));
        bool collidesWith120 = await ChunkContainsBothAsync(
            seededMoment, DateTimeOffset.UtcNow.AddDays(-120));

        (collidesWith200 || collidesWith120).ShouldBeFalse(
            $"G3: the seeded moment {seededMoment:O} must land in a chunk distinct from "
            + "RetentionRoundtripIntegrationTests' -200d/-120d seeds, or the two classes race for one archive. "
            + $"collidesWith200={collidesWith200}, collidesWith120={collidesWith120}");
    }

    private async Task<bool> ChunkContainsBothAsync(DateTimeOffset first, DateTimeOffset second)
    {
        await using AuditObservabilityDbContext context = await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> result = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'audit_events'
                  AND range_start <= {first} AND range_end > {first}
                  AND range_start <= {second} AND range_end > {second}
                """)
            .ToListAsync();
        return result[0] > 0;
    }

    /// <summary>
    /// Matches on chunk range <em>and</em> freshness. Chunk range alone is not
    /// enough: the data volume persists across runs (<c>ContainerLifetime.Persistent</c>),
    /// so a prior run's seed at the same ~400d age can already have an
    /// announcement sitting in <c>audit_events</c> whose range covers this
    /// run's <paramref name="seededMoment"/> too — a plain range match would
    /// return that stale row on the very first poll, before this run's sweep
    /// has archived anything. <paramref name="notBefore"/> — captured before
    /// this run seeded its own row — rules that out: only a row whose
    /// <c>receivedAt</c> postdates it can be this run's own announcement.
    /// </summary>
    private static async Task<JsonElement> PollForCoveringAnnouncementAsync(
        HttpClient audit, DateTimeOffset seededMoment, DateTimeOffset notBefore)
    {
        DateTime deadline = DateTime.UtcNow + AnnouncementPollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            HttpResponseMessage response = await audit.GetAsync(
                "/audit?eventKind=AuditChunkArchivedV1&pageSize=200");
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
            foreach (JsonElement row in page.GetProperty("rows").EnumerateArray())
            {
                if (row.GetProperty("receivedAt").GetDateTimeOffset() < notBefore)
                {
                    continue;
                }

                JsonElement payload = ParsePayload(row);
                DateTimeOffset from = payload.GetProperty("OccurredFrom").GetDateTimeOffset();
                DateTimeOffset until = payload.GetProperty("OccurredUntil").GetDateTimeOffset();
                if (seededMoment >= from && seededMoment < until)
                {
                    return row;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            $"No AuditChunkArchivedV1 announcement covering {seededMoment:O} and received at or after "
            + $"{notBefore:O} appeared within {AnnouncementPollTimeout.TotalSeconds:F0}s.");
    }

    /// <summary>
    /// <c>AuditRowDto.Payload</c> is the verbatim V1 JSON as a string
    /// property, so it is JSON-encoded a second time on the wire and needs a
    /// second parse — unlike <c>RetentionRoundtripIntegrationTests</c>, which
    /// reads the <c>payload::text</c> database column directly.
    /// </summary>
    private static JsonElement ParsePayload(JsonElement row) =>
        JsonDocument.Parse(row.GetProperty("payload").GetString()!).RootElement;

    private sealed record Arrangement(JsonElement Announcement, JsonElement FabCarryingRow);
}
