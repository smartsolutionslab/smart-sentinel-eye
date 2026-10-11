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
/// non-Retired Stream aggregates, and removes any MediaMTX path that no
/// longer backs a stream (orphan cleanup — covers the "stream deleted
/// while MediaMTX was down" case). A Retired stream's leftover path
/// counts as an orphan too, so this pass also finishes any removal that
/// <c>RetireStreamCommandHandler</c>'s own <c>RemovePathAsync</c> call
/// couldn't complete.
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
    IHostApplicationLifetime lifetime,
    ILogger<MediaMtxReconciler> logger) : IHostedService
{
    /// <summary>
    /// Deferred to <see cref="IHostApplicationLifetime.ApplicationStarted"/>
    /// rather than run inline here. <c>IHostedService.StartAsync</c> runs
    /// before Kestrel binds its listener (<c>WebApplicationBuilder</c>'s
    /// documented ordering), and since ADR-0171 every one of this pass's own
    /// gateway calls (<c>ListConfiguredPathsAsync</c>, <c>AddPathAsync</c>,
    /// <c>RemovePathAsync</c>) authorizes through a callback to this same
    /// host's <c>/streams/authorize</c> — so a reconcile that ran here would
    /// have MediaMTX call back to a port nothing is listening on yet, get no
    /// response, deny with 401 (or time out), and fail every call. Before this
    /// fix that failure was swallowed by the catch below and logged once as
    /// <c>ReconcilerStartupPassFailed</c>, so the reconcile silently no-opped
    /// on every single boot. Registering against <c>ApplicationStarted</c>
    /// instead waits for the listener to actually be up before the first
    /// gateway call is made.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() => _ = RunDeferredAsync());

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunDeferredAsync()
    {
        try
        {
            await ReconcileOnceAsync(lifetime.ApplicationStopping);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reconcile failure must not take the host down. Streams keep
            // working; the next restart retries.
            logger.ReconcilerStartupPassFailed(ex);
        }
    }

    public async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IDbContextFactory<StreamDistributionDbContext> factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        IRtspGateway gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

        await using StreamDistributionDbContext context =
            await factory.CreateDbContextAsync(cancellationToken);

        // Anonymous type, not ValueTuple.Create: both properties carry value
        // converters and EF cannot translate the factory call into SQL.
        var streams = await context.Streams
            .AsNoTracking()
            .Where(stream => stream.State != StreamState.Retired)
            .Select(stream => new { stream.Path, stream.SourceUrl })
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
        foreach (var entry in streams)
        {
            MediaMtxPath path = entry.Path;
            StreamSourceUrl sourceUrl = entry.SourceUrl;

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
