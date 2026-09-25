namespace SmartSentinelEye.AuditObservability.Infrastructure.Archive;

/// <summary>
/// Bound from the <c>AuditArchive</c> configuration section. The
/// Azure Blob Storage connection itself comes from the
/// Aspire-injected <c>blobs</c> connection string (spec 009
/// ADR-0101, ADR-0155).
/// </summary>
public sealed class AuditArchiveOptions
{
    public const string SectionName = "AuditArchive";

    /// <summary>Blob container the retention worker uploads to. Created on first use if missing.</summary>
    public string ContainerName { get; set; } = "audit-archive";

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
