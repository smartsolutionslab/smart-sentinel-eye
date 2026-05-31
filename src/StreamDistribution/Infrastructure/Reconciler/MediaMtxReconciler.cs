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
/// The complementary "re-add missing paths" half requires persisting the
/// camera's RTSP source URL on the Stream aggregate — non-trivial domain
/// change that's tracked separately. For now, missing paths are recovered
/// on the next CameraRegistered redelivery or operator-triggered
/// reprovisioning, which is acceptable because a missing path manifests
/// as a 404 on WHEP open (loud + recoverable) rather than a silent leak.
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
            await ReconcileOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reconcile failure must not block the host from starting.
            // Streams keep working; the next restart retries.
            logger.LogWarning(ex, "MediaMtxReconciler startup pass failed; continuing without reconcile.");
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
            await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        HashSet<MediaMtxPath> expected = (await context.Streams
            .AsNoTracking()
            .Select(stream => stream.Path)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();

        IReadOnlyList<MediaMtxPath> configured =
            await gateway.ListConfiguredPathsAsync(cancellationToken).ConfigureAwait(false);

        int removed = 0;
        foreach (MediaMtxPath path in configured)
        {
            if (expected.Contains(path)) continue;
            try
            {
                await gateway.RemovePathAsync(path, cancellationToken).ConfigureAwait(false);
                removed++;
                logger.LogInformation("Reconciler removed orphan MediaMTX path {Path}.", path);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex,
                    "Reconciler failed to remove orphan path {Path}; will retry on next restart.", path);
            }
        }

        logger.LogInformation(
            "MediaMtxReconciler startup pass complete. Configured={Configured}, expected={Expected}, removed={Removed}.",
            configured.Count, expected.Count, removed);
    }
}
