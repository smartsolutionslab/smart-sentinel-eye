namespace SmartSentinelEye.AuditObservability.Application.Retention;

/// <summary>
/// Cold-archive seam (spec 009 FR-014 / FR-016). Implementations
/// stream every row in <see cref="AuditChunk"/> to a content-
/// addressed object on MinIO and return the object key + checksum
/// for the retention worker to publish in
/// <c>AuditChunkArchivedV1</c>. Idempotent: a second call for the
/// same chunk short-circuits with <see cref="ChunkArchiveResult.AlreadyArchived"/>
/// set when an object with the expected key + ETag is already
/// present.
/// </summary>
public interface IAuditChunkArchiver
{
    Task<ChunkArchiveResult> ArchiveChunkAsync(
        AuditChunk chunk, CancellationToken cancellationToken);
}

public sealed record ChunkArchiveResult(
    string MinioObjectKey,
    string ContentMd5,
    int RowCount,
    bool AlreadyArchived);
