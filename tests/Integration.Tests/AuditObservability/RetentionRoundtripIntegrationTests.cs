using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 009 US3 (T067): a chunk aged past the 90-day boundary is archived to
/// MinIO, dropped from the hypertable, and announced with
/// <c>AuditChunkArchivedV1</c>. In the integration suite the retention worker
/// sweeps every few seconds (AppHost E2E override), so seeding a back-dated
/// row drives the round-trip.
///
/// <para>
/// Assertions read the store directly rather than the HTTP API: the worker
/// holds a brief <c>drop_chunks</c> lock during archival, and racing it from
/// the read API was flaky. The MinIO upload is verified transitively — the
/// archiver only publishes <c>AuditChunkArchivedV1</c> after a successful
/// upload + ETag round-trip, and the audit subscriber records that very V1, so
/// the recorded payload's object key + row count prove the object landed.
/// </para>
///
/// <para>
/// <b>Two chunks, not one.</b> The sweep opens a single service scope and then
/// publishes and commits once per chunk on it — the shape that lost three of
/// four announcements in #1801, where one scope was shared across a loop whose
/// body flushed the scoped outbox. A single seeded chunk cannot tell a sweep
/// that announces everything from one that announces only its first, which is
/// why this test seeds two and asserts both are announced by name.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RetentionRoundtripIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// The hypertable's chunk interval is one month, so rows this far apart
    /// land in different chunks and one sweep has two of them to do. Both are
    /// well past the 90-day boundary.
    /// </summary>
    private static readonly int[] SeededAgesInDays = [200, 120];

    [Fact]
    public async Task Every_chunk_past_the_retention_boundary_is_archived_dropped_and_announced()
    {
        DateTimeOffset[] seeded =
            [.. SeededAgesInDays.Select(age => DateTimeOffset.UtcNow.AddDays(-age))];

        foreach (DateTimeOffset occurredAt in seeded)
        {
            await SeedBackdatedRowAsync(occurredAt);
        }

        (await CountChunksPastBoundaryAsync()).ShouldBeGreaterThanOrEqualTo(2,
            "the back-dated inserts must create two chunks past the 90-day boundary, "
            + "or this test cannot distinguish announcing every chunk from announcing the first");

        IReadOnlyList<JsonElement> archived = await PollForArchivesAsync(seeded);

        // AuditChunkArchivedV1 payload (PascalCase — serialised with default
        // options) proves the MinIO upload + the archived row count.
        foreach (JsonElement announcement in archived)
        {
            announcement.GetProperty("RowCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
            announcement.GetProperty("MinioObjectKey").GetString().ShouldNotBeNullOrEmpty();
        }

        // The aged chunks are gone from the hypertable.
        (await CountChunksPastBoundaryAsync()).ShouldBe(0);
    }

    private async Task SeedBackdatedRowAsync(DateTimeOffset occurredAt)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        string emptyJson = "{}";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_events (
                audit_id, occurred_at, received_at, fab_id, event_kind,
                resource_kind, resource_identifier, actor_identifier,
                actor_username, event_identifier, payload, payload_size_bytes,
                schema_version)
            VALUES (
                {Guid.CreateVersion7()}, {occurredAt}, {DateTimeOffset.UtcNow}, NULL,
                'RetentionSeedV1', NULL, NULL, {Guid.Empty}, NULL,
                {Guid.CreateVersion7()}, {emptyJson}::jsonb, 2, 1)
            """);
    }

    private async Task<int> CountChunksPastBoundaryAsync()
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> result = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'audit_events'
                  AND range_end <= now() - INTERVAL '90 days'
                """)
            .ToListAsync();
        return result[0];
    }

    /// <summary>
    /// Waits until every seeded moment has an announcement whose chunk range
    /// covers it, and the hypertable is clear. Matching by range rather than
    /// by count is what makes the assertion specific: two announcements for
    /// the same chunk would satisfy a count and prove nothing.
    /// </summary>
    private async Task<IReadOnlyList<JsonElement>> PollForArchivesAsync(
        DateTimeOffset[] seeded)
    {
        IReadOnlyList<JsonElement> announcements = [];

        for (int attempt = 0; attempt < 60; attempt++)
        {
            announcements = await ReadArchiveAnnouncementsAsync();

            List<JsonElement> covering =
                [.. seeded.Select(moment => announcements.FirstOrDefault(a => Covers(a, moment)))
                          .Where(a => a.ValueKind is not JsonValueKind.Undefined)];

            if (covering.Count == seeded.Length && await CountChunksPastBoundaryAsync() == 0)
            {
                return covering;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        // Says which half failed, because this is adjudicated from CI logs.
        int remaining = await CountChunksPastBoundaryAsync();
        throw new Xunit.Sdk.XunitException(
            $"Seeded {seeded.Length} aged chunks; {announcements.Count} AuditChunkArchivedV1 "
            + $"announcement(s) arrived within 30s and {remaining} chunk(s) remain past the "
            + "boundary. A chunk dropped but never announced is the #1801 shape: one scope "
            + "shared across a loop whose body flushes the scoped outbox.");
    }

    private async Task<IReadOnlyList<JsonElement>> ReadArchiveAnnouncementsAsync()
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<string> payloads = await context.Database
            .SqlQuery<string>($"""
                SELECT payload::text AS "Value"
                FROM audit_events
                WHERE event_kind = 'AuditChunkArchivedV1'
                """)
            .ToListAsync();

        return [.. payloads.Select(payload => JsonDocument.Parse(payload).RootElement)];
    }

    private static bool Covers(JsonElement announcement, DateTimeOffset moment)
    {
        DateTimeOffset from = announcement.GetProperty("OccurredFrom").GetDateTimeOffset();
        DateTimeOffset until = announcement.GetProperty("OccurredUntil").GetDateTimeOffset();

        return moment >= from && moment < until;
    }
}
