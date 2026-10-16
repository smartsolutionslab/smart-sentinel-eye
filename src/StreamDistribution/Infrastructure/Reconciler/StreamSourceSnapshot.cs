using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

internal sealed class StreamSourceSnapshot(StreamDistributionDbContext context) : IStreamSourceSnapshot
{
    public async Task<IReadOnlyDictionary<MediaMtxPath, StreamSourceUrl>> ListLiveSourcesAsync(
        CancellationToken cancellationToken)
    {
        // Anonymous type: both properties carry value converters and EF cannot
        // translate a tuple factory call into SQL.
        var streams = await context.Streams
            .AsNoTracking()
            .Where(stream => stream.State != StreamState.Retired)
            .Select(stream => new { stream.Path, stream.SourceUrl })
            .ToListAsync(cancellationToken);

        return streams.ToDictionary(entry => entry.Path, entry => entry.SourceUrl);
    }

    public async Task<Option<StreamSourceUrl>> ReadCommittedSourceAsync(
        MediaMtxPath path,
        CancellationToken cancellationToken)
    {
        Ensure.That(path).IsNotNull();

        var streams = await context.Streams
            .AsNoTracking()
            .Where(stream => stream.State != StreamState.Retired && stream.Path == path)
            .Select(stream => new { stream.SourceUrl })
            .ToListAsync(cancellationToken);

        return streams.Count == 0
            ? Option<StreamSourceUrl>.None
            : Option<StreamSourceUrl>.Some(streams[0].SourceUrl);
    }
}
