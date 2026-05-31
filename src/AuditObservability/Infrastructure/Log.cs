using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.AuditObservability.Infrastructure;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Audit chunk {ChunkIdentifier} already archived at {ObjectKey}; skipping upload.")]
    public static partial void ChunkAlreadyArchived(ILogger logger, Guid chunkIdentifier, string objectKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived audit chunk {ChunkIdentifier} ({RowCount} rows) to {ObjectKey}.")]
    public static partial void ArchivedAuditChunk(ILogger logger, Guid chunkIdentifier, int rowCount, string objectKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped TimescaleDB chunks for AuditObservability up to {Until} (procedure rows: {Count}).")]
    public static partial void DroppedChunks(ILogger logger, DateTimeOffset until, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying AuditObservability EF Core migrations.")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "AuditObservability migrations applied.")]
    public static partial void MigrationsApplied(ILogger logger);
}
