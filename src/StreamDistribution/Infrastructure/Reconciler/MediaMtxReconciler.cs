using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
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
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The deferred pass, once <see cref="IHostApplicationLifetime.ApplicationStarted"/>
    /// has fired — <see langword="null"/> until then. <see cref="StopAsync"/>
    /// awaits this rather than returning immediately, so a shutdown that lands
    /// mid-pass does not dispose the scoped service provider out from under
    /// it.
    /// </summary>
    private volatile Task? deferredPass;

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
        lifetime.ApplicationStarted.Register(() => deferredPass = RunDeferredAsync());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits, bounded by <see cref="StopWaitTimeout"/>, for an in-flight pass
    /// to finish before returning. Without this, a shutdown landing mid-pass
    /// lets the host dispose the scoped service provider
    /// <see cref="ReconcileOnceAsync"/> is still using underneath it — seen as
    /// a spurious <c>ReconcilerStartupPassFailed</c> on an
    /// <see cref="ObjectDisposedException"/>, not a real reconcile failure.
    /// The bound exists so a pass that is for some reason taking too long
    /// cannot hang shutdown itself; <see cref="ReconcileOnceAsync"/> is also
    /// handed <see cref="IHostApplicationLifetime.ApplicationStopping"/> as
    /// its own cancellation token, so it is already being asked to wind down.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var pass = deferredPass;
        if (pass is null)
        {
            return;
        }

        try
        {
            await pass.WaitAsync(StopWaitTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // Either the bound elapsed or shutdown's own token fired first —
            // expected under a fast or forced shutdown, not a failure. The
            // pass's own cancellation token (ApplicationStopping) has already
            // asked it to wind down; StopAsync must not hang waiting longer
            // than StopWaitTimeout for that to happen.
            logger.ReconcilerStopWaitDidNotComplete();
        }
    }

    private async Task RunDeferredAsync()
    {
        try
        {
            await ReconcileOnceAsync(lifetime.ApplicationStopping);
        }
        catch (Exception ex) when (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // A reconcile failure must not take the host down. Streams keep
            // working; the next restart retries.
            //
            // Filtered on the lifetime's own token, not `ex is not
            // OperationCanceledException`: that exception-type filter let a
            // shutdown-caused OperationCanceledException escape this catch
            // entirely (unlogged, by design — shutdown isn't a failure), but
            // it just as readily let a *different* exception born of the same
            // shutdown (e.g. ObjectDisposedException from StopAsync's old
            // immediate return) through as if it were a genuine reconcile
            // failure. Checking "are we shutting down" directly covers both.
            logger.ReconcilerStartupPassFailed(ex);
        }
    }

    public async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var factory =
            scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        var gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

        await using var context =
            await factory.CreateDbContextAsync(cancellationToken);

        // Anonymous type, not ValueTuple.Create: both properties carry value
        // converters and EF cannot translate the factory call into SQL.
        var streams = await context.Streams
            .AsNoTracking()
            .Where(stream => stream.State != StreamState.Retired)
            .Select(stream => new { stream.Path, stream.SourceUrl })
            .ToListAsync(cancellationToken);

        var expected = streams.Select(entry => entry.Path).ToHashSet();

        var configured =
            await gateway.ListConfiguredPathsAsync(cancellationToken);

        var removed = await RemoveOrphanPathsAsync(
            gateway,
            configured,
            expected,
            cancellationToken);

        var readded = await ReaddMissingPathsAsync(
            gateway,
            configured,
            streams.Select(entry => (entry.Path, entry.SourceUrl)),
            cancellationToken);

        logger.ReconcilerStartupPassComplete(configured.Count, expected.Count, removed, readded);

        await new MediaMtxSourceRepair(logger)
            .RepairAsync(gateway, new StreamSourceSnapshot(context), cancellationToken);
    }

    private async Task<int> RemoveOrphanPathsAsync(
        IRtspGateway gateway,
        IReadOnlyList<MediaMtxPath> configured,
        HashSet<MediaMtxPath> expected,
        CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var path in configured)
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

        return removed;
    }

    private async Task<int> ReaddMissingPathsAsync(
        IRtspGateway gateway,
        IReadOnlyList<MediaMtxPath> configured,
        IEnumerable<(MediaMtxPath Path, StreamSourceUrl SourceUrl)> streams,
        CancellationToken cancellationToken)
    {
        // Re-add the other half: a Stream whose path MediaMTX no longer knows
        // about. Without this a MediaMTX restart leaves every stream 404ing,
        // because CameraRegisteredV1 does not re-fire for existing cameras.
        var present = configured.ToHashSet();

        var readded = 0;
        foreach (var (path, sourceUrl) in streams)
        {
            if (present.Contains(path))
            {
                continue;
            }

            try
            {
                await gateway.AddPathAsync(path, sourceUrl.Value, cancellationToken);
                readded++;
                logger.ReconcilerReaddedMissingPath(path, UrlRedaction.ToOrigin(sourceUrl.Value));
            }
            catch (HttpRequestException ex)
            {
                logger.ReconcilerFailedToReaddMissingPath(ex, path);
            }
        }

        return readded;
    }
}
