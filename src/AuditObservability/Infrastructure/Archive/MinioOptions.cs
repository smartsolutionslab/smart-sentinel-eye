namespace SmartSentinelEye.AuditObservability.Infrastructure.Archive;

/// <summary>
/// Bound from the <c>Minio</c> configuration section + the
/// Aspire-injected MinIO connection string (spec 009 ADR-0101).
/// </summary>
public sealed class MinioOptions
{
    public const string SectionName = "Minio";

    /// <summary>Bucket the retention worker uploads to. Created on first use if missing.</summary>
    public string Bucket { get; set; } = "audit-archive";

    /// <summary>
    /// Path template for archived chunks. <c>{fab}</c> resolves
    /// to either the originating fab id or <c>_unscoped</c>;
    /// <c>{year}</c> / <c>{month}</c> use UTC values from the
    /// chunk's <c>OccurredFrom</c>; <c>{chunkId}</c> is the
    /// stable identifier from <see cref="Application.Retention.AuditChunk"/>.
    /// </summary>
    public string ObjectKeyTemplate { get; set; } =
        "fab={fab}/year={year:0000}/month={month:00}/chunk-{chunkId:N}.ndjson.gz";
}
