using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Audited {EventKind} {EventIdentifier} (resource: {ResourceKind}/{ResourceIdentifier}).")]
    public static partial void Audited(ILogger logger, string eventKind, EventIdentifier eventIdentifier, string resourceKind, string resourceIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit retention worker started with window {Window} and tick interval {Interval}.")]
    public static partial void RetentionWorkerStarted(ILogger logger, TimeSpan window, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retention sweep at {Boundary}: no chunks past the boundary.")]
    public static partial void RetentionSweepNoChunks(ILogger logger, DateTimeOffset boundary);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention sweep at {Boundary}: {ChunkCount} chunk(s) to archive.")]
    public static partial void RetentionSweepChunksToArchive(ILogger logger, DateTimeOffset boundary, int chunkCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived chunk {ChunkIdentifier} ({RowCount} rows, already-archived={AlreadyArchived}) to {ObjectKey}.")]
    public static partial void ArchivedChunk(ILogger logger, Guid chunkIdentifier, int rowCount, bool alreadyArchived, string objectKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to archive chunk {ChunkIdentifier}; leaving it in place for the next sweep.")]
    public static partial void ArchiveChunkFailed(ILogger logger, Exception exception, Guid chunkIdentifier);
}
