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
/// </summary>
[Collection(AspireCollection.Name)]
public class RetentionRoundtripIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_chunk_past_the_retention_boundary_is_archived_dropped_and_announced()
    {
        // Seed one audit row ~200 days old → lands in a chunk well past the
        // 90-day retention boundary, so the next sweep archives + drops it.
        await SeedBackdatedRowAsync(DateTimeOffset.UtcNow.AddDays(-200));

        (await CountChunksPastBoundaryAsync()).ShouldBeGreaterThanOrEqualTo(1,
            "the back-dated insert must create a chunk past the 90-day boundary");

        JsonElement archived = await PollForArchiveAsync();

        // AuditChunkArchivedV1 payload (PascalCase — serialised with default
        // options) proves the MinIO upload + the archived row count.
        archived.GetProperty("RowCount").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        archived.GetProperty("MinioObjectKey").GetString().ShouldNotBeNullOrEmpty();

        // The aged chunk is gone from the hypertable.
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

    private async Task<JsonElement> PollForArchiveAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            await using AuditObservabilityDbContext context =
                await aspire.CreateAuditObservabilityDbContextAsync();

            List<string> payloads = await context.Database
                .SqlQuery<string>($"""
                    SELECT payload::text AS "Value"
                    FROM audit_events
                    WHERE event_kind = 'AuditChunkArchivedV1'
                    LIMIT 1
                    """)
                .ToListAsync();

            if (payloads.Count == 1 && await CountChunksPastBoundaryAsync() == 0)
            {
                return JsonDocument.Parse(payloads[0]).RootElement;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new Xunit.Sdk.XunitException(
            "The aged chunk was not archived + dropped + announced within 30s.");
    }
}
