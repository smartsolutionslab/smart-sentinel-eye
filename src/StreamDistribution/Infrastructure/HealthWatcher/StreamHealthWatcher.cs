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
        ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>> handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ReportStreamHealthCommand, Result<StreamState, ReportStreamHealthError>>>();

        await using StreamDistributionDbContext context = await factory.CreateDbContextAsync(cancellationToken);

        List<(Guid Camera, MediaMtxPath Path, StreamState State)> streams = await context.Streams
            .AsNoTracking()
            .Select(stream => new ValueTuple<Guid, MediaMtxPath, StreamState>(stream.Camera.Value, stream.Path, stream.State))
            .ToListAsync(cancellationToken);

        DateTimeOffset now = clock.UtcNow;

        foreach ((Guid cameraGuid, MediaMtxPath path, StreamState state) in streams)
        {
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
