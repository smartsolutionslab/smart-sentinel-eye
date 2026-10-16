using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// The database reads <see cref="MediaMtxSourceRepair"/> needs, behind a seam so
/// the pass is testable without a database (ADR-0103).
/// </summary>
internal interface IStreamSourceSnapshot
{
    /// <summary>The committed source of every non-retired stream, by path.</summary>
    Task<IReadOnlyDictionary<MediaMtxPath, StreamSourceUrl>> ListLiveSourcesAsync(CancellationToken cancellationToken);

    /// <summary>The committed source of one path; none when the stream is retired or gone.</summary>
    Task<Option<StreamSourceUrl>> ReadCommittedSourceAsync(MediaMtxPath path, CancellationToken cancellationToken);
}
