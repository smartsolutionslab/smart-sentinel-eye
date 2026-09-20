using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Audit chunk {ChunkIdentifier} already archived at {ObjectKey}; skipping upload.")]
    public static partial void ChunkAlreadyArchived(this ILogger logger, Guid chunkIdentifier, string objectKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived audit chunk {ChunkIdentifier} ({RowCount} rows) to {ObjectKey}.")]
    public static partial void ArchivedAuditChunk(this ILogger logger, Guid chunkIdentifier, int rowCount, string objectKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped audit chunk {ChunkIdentifier} ({ChunkName}) from TimescaleDB.")]
    public static partial void DroppedChunk(this ILogger logger, Guid chunkIdentifier, string chunkName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit chunk {ChunkIdentifier} ({OccurredFrom} - {OccurredUntil}) was already dropped from TimescaleDB; drop_chunks returned no rows.")]
    public static partial void ChunkAlreadyDropped(this ILogger logger, Guid chunkIdentifier, DateTimeOffset occurredFrom, DateTimeOffset occurredUntil);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropping audit chunk {ChunkIdentifier} removed {Count} TimescaleDB chunks instead of exactly one: {ChunkNames}. The drop has already committed; this indicates the chunk window no longer isolates a single chunk.")]
    public static partial void DroppedMoreThanOneChunk(this ILogger logger, Guid chunkIdentifier, int count, string chunkNames);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying AuditObservability EF Core migrations.")]
    public static partial void ApplyingMigrations(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "AuditObservability migrations applied.")]
    public static partial void MigrationsApplied(this ILogger logger);
}
