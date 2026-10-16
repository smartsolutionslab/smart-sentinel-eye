using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// Spec 378 (#2960): points a MediaMTX path back at the source the database
/// records when the two differ — a retried PATCH can land after a newer
/// repoint. Writes MediaMTX only, and only ever the database's value.
///
/// <para>
/// MediaMTX is read before the database, so a repoint that commits between the
/// two reads is seen by the second and the pass writes the newer value rather
/// than a stale one. After each patch the stream's committed source is read
/// again (the source, not the aggregate version, which the health watcher bumps
/// every few seconds) and patched once more if it moved: at most two patches
/// per path per pass.
/// </para>
/// </summary>
internal sealed class MediaMtxSourceRepair(ILogger logger)
{
    public async Task<int> RepairAsync(
        IRtspGateway gateway,
        IStreamSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        Ensure.That(gateway).IsNotNull();
        Ensure.That(snapshot).IsNotNull();

        var observed = await gateway.ListConfiguredPathSourcesAsync(cancellationToken);
        var persisted = await snapshot.ListLiveSourcesAsync(cancellationToken);

        var repaired = 0;
        foreach (var (path, source) in persisted)
        {
            if (!observed.TryGetValue(path, out var observedSource)
                || string.Equals(observedSource, source.Value, StringComparison.Ordinal))
            {
                continue;
            }

            Divergence divergence = new(path, observedSource, source);
            if (await TryRepairPathAsync(gateway, snapshot, divergence, cancellationToken))
            {
                repaired++;
            }
        }

        return repaired;
    }

    private readonly record struct Divergence(MediaMtxPath Path, string ObservedSource, StreamSourceUrl PersistedSource);

    private async Task<bool> TryRepairPathAsync(
        IRtspGateway gateway,
        IStreamSourceSnapshot snapshot,
        Divergence divergence,
        CancellationToken cancellationToken)
    {
        var (path, observedSource, persistedSource) = divergence;

        try
        {
            await gateway.RepointPathAsync(path, persistedSource.Value, cancellationToken);
            logger.ReconcilerRepairedPathSource(
                path,
                UrlRedaction.ToOrigin(observedSource),
                UrlRedaction.ToOrigin(persistedSource.Value));

            var committed = await snapshot.ReadCommittedSourceAsync(path, cancellationToken);
            if (committed.HasValue
                && !string.Equals(committed.Value.Value, persistedSource.Value, StringComparison.Ordinal))
            {
                await gateway.RepointPathAsync(path, committed.Value.Value, cancellationToken);
                logger.ReconcilerRepairedPathSource(
                    path,
                    UrlRedaction.ToOrigin(persistedSource.Value),
                    UrlRedaction.ToOrigin(committed.Value.Value));
            }

            return true;
        }
        catch (HttpRequestException ex)
        {
            logger.ReconcilerFailedToRepairPathSource(ex, path);
            return false;
        }
    }
}
