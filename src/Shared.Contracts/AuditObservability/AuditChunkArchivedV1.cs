namespace SmartSentinelEye.Shared.Contracts.AuditObservability;

/// <summary>
/// Integration event raised by AuditObservability (spec 009)
/// after a hypertable chunk that has crossed the 90-day retention
/// boundary is successfully exported to MinIO and dropped from
/// PostgreSQL. Carries the MinIO object key + content checksum so
/// downstream subscribers (observability dashboards, runbook
/// alerting) can audit + locate the archive without querying
/// the database.
/// </summary>
public sealed record AuditChunkArchivedV1(
    Guid ChunkIdentifier,
    string? FabId,
    int RowCount,
    DateTimeOffset OccurredFrom,
    DateTimeOffset OccurredUntil,
    DateTimeOffset ArchivedAt,
    string MinioObjectKey,
    string ContentMd5) : IIntegrationEvent;
