namespace SmartSentinelEye.AuditObservability.Application.Retention;

/// <summary>
/// Discovers TimescaleDB chunks past the retention boundary +
/// drops them after their cold archive lands (spec 009 FR-013 /
/// FR-016). Implemented by the Infrastructure layer against
/// TimescaleDB's <c>show_chunks</c> / <c>drop_chunks</c> stored
/// procedures.
/// </summary>
public interface IAuditChunkInventory
{
    Task<IReadOnlyList<AuditChunk>> ListChunksOlderThanAsync(
        DateTimeOffset boundary, CancellationToken cancellationToken);

    Task DropChunkAsync(AuditChunk chunk, CancellationToken cancellationToken);
}

/// <summary>
/// One TimescaleDB chunk that has crossed the retention boundary.
/// </summary>
public sealed record AuditChunk(
    Guid ChunkIdentifier,
    DateTimeOffset OccurredFrom,
    DateTimeOffset OccurredUntil);
