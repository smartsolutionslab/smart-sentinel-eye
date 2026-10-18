using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.HealthWatcher;

/// <summary>
/// Periodic poll of MediaMTX path health (spec 002 T061 / FR-008). Every
/// <see cref="PollInterval"/> the watcher lists every stream, asks MediaMTX
/// for its current state, and dispatches a <see cref="ReportStreamHealthCommand"/>
/// when the observation would cause an aggregate transition. The aggregate
/// itself decides whether the change raises an integration event.
///
/// Per-stream timing tracking (5-minute Offline window) lives in this
/// service so the aggregate stays free of wall-clock logic.
///
/// A path present in the previous successful sweep and missing from this one is
/// held for a tick rather than reported as not registered.
/// </summary>
public sealed class StreamHealthWatcher(IServiceScopeFactory scopeFactory, IClock clock, ILogger<StreamHealthWatcher> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    private readonly Dictionary<Guid, DateTimeOffset> degradedSince = [];
    private HashSet<MediaMtxPath> pathsPresentInLastSweep = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.StreamHealthWatcherStarted(PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.StreamHealthWatcherPollFailed(ex);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        var gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        // Retired streams are excluded from the sweep, not merely reported on
        // (spec 028, research §4). Their MediaMTX path has been removed, so a
        // probe fails — and since #1801 the watcher announces *every* health
        // change rather than one per sweep, which would make each retired
        // camera a permanent source of health announcements and audit rows for
        // hardware that does not exist. Filtered in the query so the rows are
        // never loaded; DispatchAsync skips them again, because that is where it
        // can be asserted without a database (ADR-0103).
        List<(Guid Camera, MediaMtxPath Path, StreamState State)> streams = await context.Streams
            .AsNoTracking()
            .Where(stream => stream.State != StreamState.Retired)
            .Select(stream => new ValueTuple<Guid, MediaMtxPath, StreamState>(stream.Camera.Value, stream.Path, stream.State))
            .ToListAsync(cancellationToken);

        await DispatchAsync(streams, gateway, cancellationToken);
    }

    /// <summary>
    /// The dispatch half of a sweep, separated from the read so the scoping
    /// below can be asserted without a database (#1801; ADR-0103 rules out both
    /// an in-memory provider and Testcontainers).
    /// </summary>
    internal async Task DispatchAsync(
        IReadOnlyList<(Guid Camera, MediaMtxPath Path, StreamState State)> streams,
        IRtspGateway gateway,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // A handful of calls for the whole sweep (one per MediaMTX page of
        // paths, not one per stream). Every MediaMTX control-API call now
        // authorizes through the same rate-limited /streams/authorize hook a
        // real WHEP viewer's open uses (ADR-0171), so probing per path would
        // have scaled the hook-call rate with the camera count instead of
        // the poll interval — see IRtspGateway.GetAllPathsHealthAsync's doc
        // comment.
        IReadOnlyDictionary<MediaMtxPath, RtspPathHealth> observed;
        try
        {
            observed = await gateway.GetAllPathsHealthAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // The whole tick is lost, not one camera — there is only one HTTP
            // call left to fail. The next tick, two seconds later, tries again.
            logger.HealthSweepFailed(ex);
            return;
        }

        var previouslyPresent = pathsPresentInLastSweep;
        pathsPresentInLastSweep = [.. observed.Keys];

        foreach ((var cameraGuid, var path, var state) in streams)
        {
            // Before the lookup, so a retired stream costs no scope. The
            // listing query already excludes these; this is the half that a
            // unit test can reach, and it also closes the race where a stream
            // is retired between the read and the dispatch.
            if (state == StreamState.Retired)
            {
                degradedSince.Remove(cameraGuid);
                continue;
            }

            // Offset pagination shifts names under a concurrent removal, so a
            // single absence of a path seen on the previous sweep is not
            // evidence; a second consecutive absence is reported as before.
            if (IsHeldAbsence(path, observed, previouslyPresent))
            {
                continue;
            }

            // A path absent from the batch reads the same way a 404 from the
            // old per-path probe did: not registered, not a probe failure.
            var observation = observed.TryGetValue(path, out var found)
                ? found
                : new RtspPathHealth(IsReady: false, LastError: "path not registered", LastFrameAt: null, DetectedMode: TranscodeMode.Unknown);

            var declareOffline = ShouldDeclareOffline(cameraGuid, observation, state, now);

            await ReportInOwnScopeAsync(new(CameraIdentifier.From(cameraGuid), observation, declareOffline), cancellationToken);
        }
    }

    private async Task ReportInOwnScopeAsync(ReportStreamHealthCommand command, CancellationToken cancellationToken)
    {
        // A scope per camera, not one for the sweep (#1801). The handler's
        // repository publishes through the scoped outbox and then flushes it;
        // reusing one scope across the loop meant every camera after the
        // first published into an already-flushed message context. Those
        // announcements were dropped with no exception, no outbox row and
        // nothing in the log — four cameras changing state delivered one
        // StreamHealthChangedV1.
        //
        // PersistenceLoopHostedService has always opened a scope per command
        // for this reason, which is why event ingestion never showed it.
        await using var perCamera = scopeFactory.CreateAsyncScope();
        var handler =
            perCamera.ServiceProvider.GetRequiredService<ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>>();

        await handler.HandleAsync(command, cancellationToken);
    }

    private static bool IsHeldAbsence(
        MediaMtxPath path,
        IReadOnlyDictionary<MediaMtxPath, RtspPathHealth> observed,
        HashSet<MediaMtxPath> previouslyPresent)
        => !observed.ContainsKey(path) && previouslyPresent.Contains(path);

    private bool ShouldDeclareOffline(Guid camera, RtspPathHealth observation, StreamState currentState, DateTimeOffset now)
    {
        if (observation.IsReady)
        {
            degradedSince.Remove(camera);
            return false;
        }

        if (currentState == StreamState.Healthy || currentState == StreamState.Provisioning)
        {
            degradedSince[camera] = now;
            return false;
        }

        if (!degradedSince.TryGetValue(camera, out var degradedAt))
        {
            degradedSince[camera] = now;
            return false;
        }

        return now - degradedAt >= OfflineAfter;
    }
}
