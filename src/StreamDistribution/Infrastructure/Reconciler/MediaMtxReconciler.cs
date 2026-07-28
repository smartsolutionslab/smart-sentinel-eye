using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// Spec 002 T085 — one-shot startup pass that reconciles MediaMTX paths
/// against the StreamDistribution DB.
///
/// <para>
/// On boot, lists every canonical (<c>cam-{guid}</c>) path configured in
/// MediaMTX, compares it against the set of paths currently held by
/// Stream aggregates, and removes any MediaMTX path that no longer backs
/// a stream (orphan cleanup — covers the "stream deleted while MediaMTX
/// was down" case).
/// </para>
///
/// <para>
/// It also re-adds the complementary half: any Stream whose path is absent
/// from MediaMTX is re-created from the <see cref="StreamSourceUrl"/>
/// persisted on the aggregate. Before that URL was persisted (#197) the
/// reconciler knew a path's name but not what to point it at, so a MediaMTX
/// restart left every stream 404ing on WHEP open until a CameraRegistered
/// redelivery happened to re-provision it — which never fires for cameras
/// that already exist.
/// </para>
/// </summary>
public sealed class MediaMtxReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaMtxReconciler> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileOnceAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reconcile failure must not block the host from starting.
            // Streams keep working; the next restart retries.
            logger.ReconcilerStartupPassFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IDbContextFactory<StreamDistributionDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        IRtspGateway gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

        await using StreamDistributionDbContext context =
            await factory.CreateDbContextAsync(cancellationToken);

        List<(MediaMtxPath Path, StreamSourceUrl SourceUrl)> streams = await context.Streams
            .AsNoTracking()
            .Select(stream => ValueTuple.Create(stream.Path, stream.SourceUrl))
            .ToListAsync(cancellationToken);

        HashSet<MediaMtxPath> expected = streams.Select(entry => entry.Path).ToHashSet();

        IReadOnlyList<MediaMtxPath> configured =
            await gateway.ListConfiguredPathsAsync(cancellationToken);

        int removed = 0;
        foreach (MediaMtxPath path in configured)
        {
            if (expected.Contains(path))
            {
                continue;
            }

            try
            {
                await gateway.RemovePathAsync(path, cancellationToken);
                removed++;
                logger.ReconcilerRemovedOrphanPath(path);
            }
            catch (HttpRequestException ex)
            {
                logger.ReconcilerFailedToRemoveOrphanPath(ex, path);
            }
        }

        // Re-add the other half: a Stream whose path MediaMTX no longer knows
        // about. Without this a MediaMTX restart leaves every stream 404ing,
        // because CameraRegisteredV1 does not re-fire for existing cameras.
        HashSet<MediaMtxPath> present = configured.ToHashSet();

        int readded = 0;
        foreach ((MediaMtxPath path, StreamSourceUrl sourceUrl) in streams)
        {
            if (present.Contains(path))
            {
                continue;
            }

            try
            {
                await gateway.AddPathAsync(path, sourceUrl.Value, cancellationToken);
                readded++;
                logger.ReconcilerReaddedMissingPath(path, sourceUrl.Value);
            }
            catch (HttpRequestException ex)
            {
                logger.ReconcilerFailedToReaddMissingPath(ex, path);
            }
        }

        logger.ReconcilerStartupPassComplete(configured.Count, expected.Count, removed, readded);
    }
}
