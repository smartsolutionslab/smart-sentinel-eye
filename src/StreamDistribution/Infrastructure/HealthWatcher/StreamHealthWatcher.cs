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
/// </summary>
public sealed class StreamHealthWatcher(IServiceScopeFactory scopeFactory, IClock clock, ILogger<StreamHealthWatcher> logger)
    : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);

    private readonly Dictionary<Guid, DateTimeOffset> degradedSince = [];

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
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IDbContextFactory<StreamDistributionDbContext> factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
        IRtspGateway gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

        await using StreamDistributionDbContext context = await factory.CreateDbContextAsync(cancellationToken);

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
        DateTimeOffset now = clock.UtcNow;

        foreach ((Guid cameraGuid, MediaMtxPath path, StreamState state) in streams)
        {
            // Before the probe, so a retired stream costs neither an HTTP call
            // to a path that no longer exists nor a scope. The listing query
            // already excludes these; this is the half that a unit test can
            // reach, and it also closes the race where a stream is retired
            // between the read and the dispatch.
            if (state == StreamState.Retired)
            {
                degradedSince.Remove(cameraGuid);
                continue;
            }

            RtspPathHealth observation;
            try
            {
                observation = await gateway.GetPathHealthAsync(path, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                logger.HealthProbeFailed(ex, path);
                continue;
            }

            bool declareOffline = ShouldDeclareOffline(cameraGuid, observation, state, now);
            ReportStreamHealthCommand command = new(CameraIdentifier.From(cameraGuid), observation, declareOffline);

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
            await using AsyncServiceScope perCamera = scopeFactory.CreateAsyncScope();
            ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>> handler =
                perCamera.ServiceProvider.GetRequiredService<ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>>();

            await handler.HandleAsync(command, cancellationToken);
        }
    }

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

        if (!degradedSince.TryGetValue(camera, out DateTimeOffset degradedAt))
        {
            degradedSince[camera] = now;
            return false;
        }

        return now - degradedAt >= OfflineAfter;
    }
}
