using System.Globalization;
using System.Net;
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
    private readonly MediaMtxSourceRepair repair = new(logger, timeProvider);
    private readonly WarningRepeatGate<SweepFailure> failures = new(timeProvider);

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

            await repair.RepairAsync(gateway, new StreamSourceSnapshot(context), stoppingToken);
            failures.Clear();
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogFailure(ex);
        }
    }

    private void LogFailure(Exception failure)
    {
        var http = failure as HttpRequestException;
        SweepFailure outcome = new(failure.GetType(), http?.StatusCode, http?.HttpRequestError);

        if (failures.ShouldWarn(outcome))
        {
            logger.SourceRepairSweepFailed(failure);
            return;
        }

        var status = outcome.Status.HasValue
            ? ((int)outcome.Status.Value).ToString(CultureInfo.InvariantCulture)
            : "-";
        logger.SourceRepairSweepStillFailing(outcome.ExceptionType.Name, status);
    }

    private readonly record struct SweepFailure(Type ExceptionType, HttpStatusCode? Status, HttpRequestError? Error);
}
