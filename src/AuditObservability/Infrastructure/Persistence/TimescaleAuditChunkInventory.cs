using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.AuditObservability.Application.Retention;

namespace SmartSentinelEye.AuditObservability.Infrastructure.Persistence;

/// <summary>
/// Production <see cref="IAuditChunkInventory"/> backed by
/// TimescaleDB's <c>timescaledb_information.chunks</c> view +
/// <c>drop_chunks</c> stored procedure. Each Timescale chunk is
/// surfaced as one <see cref="AuditChunk"/> with its closed/open
/// time range so the archiver can stream just the rows in that
/// window.
/// </summary>
public sealed class TimescaleAuditChunkInventory(
    IDbContextFactory<AuditObservabilityDbContext> dbContextFactory,
    ILogger<TimescaleAuditChunkInventory> logger) : IAuditChunkInventory
{
    public async Task<IReadOnlyList<AuditChunk>> ListChunksOlderThanAsync(
        DateTimeOffset boundary, CancellationToken cancellationToken)
    {
        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // `range_end` is exclusive in TimescaleDB's chunk view —
        // a chunk has fully aged out only once it's strictly
        // less than the retention boundary.
        IReadOnlyList<ChunkRow> rows = await context.Database
            .SqlQuery<ChunkRow>(
                $"""
                SELECT
                    chunk_name AS "ChunkName",
                    range_start AS "RangeStart",
                    range_end AS "RangeEnd"
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'audit_events'
                  AND range_end <= {boundary}
                ORDER BY range_end ASC
                """)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new AuditChunk(
            DeterministicChunkIdentifier(row.ChunkName),
            row.RangeStart,
            row.RangeEnd))];
    }

    public async Task DropChunkAsync(AuditChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        await using AuditObservabilityDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // `drop_chunks` accepts a window and drops every chunk whose
        // range_end <= older_than. Passing the chunk's exact end
        // bounds the drop to one chunk per call.
        int count = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT drop_chunks('audit_events', older_than => {chunk.OccurredUntil})
            """,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Dropped TimescaleDB chunks for AuditObservability up to {Until} (procedure rows: {Count}).",
            chunk.OccurredUntil, count);
    }

    /// <summary>
    /// Stable Guid derived from the Timescale chunk name so the
    /// same chunk surfaces with the same identifier across
    /// retention runs (MinIO object keys are content-addressed
    /// via this id; we need it deterministic).
    /// </summary>
    private static Guid DeterministicChunkIdentifier(string chunkName)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(chunkName));
        byte[] guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private sealed record ChunkRow(string ChunkName, DateTimeOffset RangeStart, DateTimeOffset RangeEnd);
}
