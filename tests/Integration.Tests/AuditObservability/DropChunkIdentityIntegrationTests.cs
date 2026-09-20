using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Spec 199 (#2425), SC-1. <c>TimescaleAuditChunkInventory.DropChunkAsync</c>
/// calls <c>drop_chunks(older_than =&gt; chunk.OccurredUntil)</c> with no lower
/// bound, so it drops every chunk whose <c>range_end</c> is at or before that
/// instant — not just the one chunk it was asked to drop. When an older chunk
/// was left behind because its archive failed, dropping a later chunk takes
/// the older one with it, silently and permanently.
///
/// <para>
/// This drives <c>DropChunkAsync</c> directly against a real TimescaleDB
/// instance rather than through <c>AuditRetentionHostedService</c>: the
/// worker runs out of process in the Aspire fixture (no seam to call
/// <c>RunOnceAsync</c> in-process) and there is no fault-injection seam for
/// the archiver, so a sweep-driven test cannot fail an archive at all. The
/// sweep's half of this story — an archive failure leaves its chunk in place
/// for the next sweep — is already pinned by
/// <c>Archiver_failure_leaves_the_chunk_in_place_for_next_sweep</c>. A test
/// that only counted chunks, or asserted against the bounds it just
/// constructed, would pass on today's broken SQL for the wrong reason — see
/// the premise assertion below and the fresh re-read after the drop.
/// </para>
///
/// <para>
/// <c>FakeAuditChunkInventory</c> already drops by identity — the
/// intended contract, and exactly why no unit test caught this — so it is not
/// touched here. This regression lives where the untested code is.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class DropChunkIdentityIntegrationTests(AspireFixture aspire)
{
    private const string SeedEventKind = "DropIdentitySeedV1";

    /// <summary>
    /// Exactly one 30-day chunk interval apart, so the two rows land in
    /// adjacent, contiguous chunks (<c>older.RangeEnd == newer.RangeStart</c>)
    /// — the shared-boundary case where a <c>newer_than</c> off-by-one would
    /// show. Both stay inside the ambient 90-day retention window, so the
    /// production retention worker (which sweeps every few seconds in this
    /// environment) never lists either chunk and cannot race this test, and
    /// nothing aged past the boundary is left behind for
    /// <c>RetentionRoundtripIntegrationTests</c>'s whole-hypertable
    /// <c>ShouldBe(0)</c> to trip over.
    /// </summary>
    private static readonly DateTimeOffset OlderOccurredAt = DateTimeOffset.UtcNow.AddDays(-85);
    private static readonly DateTimeOffset NewerOccurredAt = DateTimeOffset.UtcNow.AddDays(-55);

    [Fact]
    public async Task Dropping_a_chunk_leaves_the_older_unarchived_chunk_in_place()
    {
        Guid olderAuditId = await SeedBackdatedRowAsync(OlderOccurredAt);
        Guid newerAuditId = await SeedBackdatedRowAsync(NewerOccurredAt);

        // Assert the premise before acting (plan.md R1): if the two seeded
        // instants happened to land in one chunk, nothing could be
        // over-dropped and this test would pass on today's code for the
        // wrong reason — reading as "already fixed" when it is not.
        IReadOnlyList<ChunkRow> beforeDrop = await ReadChunksAsync();
        ChunkRow olderChunk = ChunkContaining(beforeDrop, OlderOccurredAt,
            "the older seeded instant");
        ChunkRow newerChunk = ChunkContaining(beforeDrop, NewerOccurredAt,
            "the newer seeded instant");

        olderChunk.ChunkName.ShouldNotBe(newerChunk.ChunkName,
            "the two seeded instants must land in two distinct chunks, or dropping one "
            + "chunk can never be observed taking the other with it");
        olderChunk.RangeEnd.ShouldBe(newerChunk.RangeStart,
            "the two chunks must be contiguous (older.RangeEnd == newer.RangeStart) — "
            + "that shared boundary instant is exactly where a newer_than off-by-one "
            + "would show, and a gap between them would make this test pass for a "
            + "weaker reason");

        TimescaleAuditChunkInventory inventory = new(
            new ConnectionStringDbContextFactory(await ConnectionStringAsync()),
            NullLogger<TimescaleAuditChunkInventory>.Instance);

        // The identifier is not read by DropChunkAsync's SQL (today or after
        // the fix) — only OccurredFrom/OccurredUntil are. A fresh Guid keeps
        // this test from depending on the production hashing scheme.
        AuditChunk newerChunkToDrop = new(
            Guid.CreateVersion7(), newerChunk.RangeStart, newerChunk.RangeEnd);

        await inventory.DropChunkAsync(newerChunkToDrop, default);

        // Re-read fresh from the database rather than from `beforeDrop` or
        // anything else this test wrote (plan.md R2) — otherwise the
        // assertion would be checking its own input and could not fail.
        IReadOnlyList<ChunkRow> afterDrop = await ReadChunksAsync();

        afterDrop.Select(row => row.ChunkName).ShouldContain(olderChunk.ChunkName,
            "chunk A was never archived, so dropping chunk B must not take it with it — "
            + "on today's unbounded `older_than` call, this is exactly what fails");
        (await CountRowsWithAuditIdAsync(olderAuditId)).ShouldBe(1,
            "the older chunk's own audit row must still be readable from audit_events");

        afterDrop.Select(row => row.ChunkName).ShouldNotContain(newerChunk.ChunkName,
            "the chunk that was actually named in the DropChunkAsync call must be gone");
        (await CountRowsWithAuditIdAsync(newerAuditId)).ShouldBe(0,
            "the dropped chunk's own row must no longer be readable");
    }

    private static ChunkRow ChunkContaining(
        IReadOnlyList<ChunkRow> chunks, DateTimeOffset instant, string label)
    {
        ChunkRow? match = chunks.FirstOrDefault(
            row => instant >= row.RangeStart && instant < row.RangeEnd);

        return match ?? throw new Xunit.Sdk.XunitException(
            $"No chunk in timescaledb_information.chunks covers {label} ({instant:O}). "
            + "Seeded chunks: "
            + string.Join(", ", chunks.Select(row => $"{row.ChunkName} [{row.RangeStart:O}, {row.RangeEnd:O})")));
    }

    private async Task<Guid> SeedBackdatedRowAsync(DateTimeOffset occurredAt)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        Guid auditId = Guid.CreateVersion7();
        string emptyJson = "{}";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_events (
                audit_id, occurred_at, received_at, fab_id, event_kind,
                resource_kind, resource_identifier, actor_identifier,
                actor_username, event_identifier, payload, payload_size_bytes,
                schema_version)
            VALUES (
                {auditId}, {occurredAt}, {DateTimeOffset.UtcNow}, NULL,
                {SeedEventKind}, NULL, NULL, {Guid.Empty}, NULL,
                {Guid.CreateVersion7()}, {emptyJson}::jsonb, 2, 1)
            """);

        return auditId;
    }

    private async Task<IReadOnlyList<ChunkRow>> ReadChunksAsync()
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        return await context.Database
            .SqlQuery<ChunkRow>(
                $"""
                SELECT
                    chunk_name AS "ChunkName",
                    range_start AS "RangeStart",
                    range_end AS "RangeEnd"
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'audit_events'
                ORDER BY range_end ASC
                """)
            .ToListAsync();
    }

    private async Task<int> CountRowsWithAuditIdAsync(Guid auditId)
    {
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        List<int> result = await context.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value"
                FROM audit_events
                WHERE audit_id = {auditId}
                """)
            .ToListAsync();
        return result[0];
    }

    private async Task<string> ConnectionStringAsync()
    {
        string? connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.AuditObservabilityConnectionName);

        return connectionString
            ?? throw new InvalidOperationException(
                $"Connection string '{AspireFixture.AuditObservabilityConnectionName}' was not provisioned by Aspire.");
    }

    /// <summary>
    /// <c>TimescaleAuditChunkInventory</c> needs an
    /// <c>IDbContextFactory&lt;AuditObservabilityDbContext&gt;</c>; the fixture
    /// exposes a context, not a factory. This is a test-local seam only —
    /// <c>AspireFixture</c> itself gains nothing from carrying it.
    /// </summary>
    private sealed class ConnectionStringDbContextFactory(string connectionString)
        : IDbContextFactory<AuditObservabilityDbContext>
    {
        public AuditObservabilityDbContext CreateDbContext()
        {
            DbContextOptionsBuilder<AuditObservabilityDbContext> optionsBuilder = new();
            optionsBuilder.UseNpgsql(connectionString);
            return new AuditObservabilityDbContext(optionsBuilder.Options);
        }
    }

    private sealed record ChunkRow(string ChunkName, DateTimeOffset RangeStart, DateTimeOffset RangeEnd);
}
