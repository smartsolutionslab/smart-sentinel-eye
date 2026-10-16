using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// Spec 378 (#2960): runs <see cref="MediaMtxSourceRepair"/> every
/// <see cref="MediaMtxSourceRepairOptions.Interval"/>. The startup pass covers
/// t=0, so the first sweep is one interval after the application started — and
/// never before it: since ADR-0171 every gateway call authorizes through this
/// host's own listener, which is not bound until
/// <see cref="IHostApplicationLifetime.ApplicationStarted"/>.
/// </summary>
public sealed class MediaMtxSourceRepairSweep(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IOptions<MediaMtxSourceRepairOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaMtxSourceRepairSweep> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitForApplicationStartedAsync(stoppingToken);

            using PeriodicTimer timer = new(options.Value.Interval, timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunPassAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var startedRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
    }

    private async Task RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var factory =
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<StreamDistributionDbContext>>();
            var gateway = scope.ServiceProvider.GetRequiredService<IRtspGateway>();

            await using var context = await factory.CreateDbContextAsync(stoppingToken);

            await new MediaMtxSourceRepair(logger)
                .RepairAsync(gateway, new StreamSourceSnapshot(context), stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.SourceRepairSweepFailed(ex);
        }
    }
}
