using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Drives <see cref="OrphanedClientSweep"/> at startup and then hourly
/// (spec 320 §4.4). Unlike <see cref="KioskPrivilegeSweepHostedService"/>,
/// which runs once because a boot is a natural point to repair a residue, an
/// orphan blocks an operator <i>now</i> and a 24/7 Identity API may not
/// restart for weeks — so this keeps ticking, the shape of
/// <c>IdempotencyReservationSweepHostedService</c> (spec 320 plan §5).
///
/// <para>
/// A wrapper rather than making the pass itself a hosted service, for
/// <see cref="KioskPrivilegeSweepHostedService"/>'s reason: that would drag
/// <c>Microsoft.Extensions.Hosting</c> into Application.
/// </para>
/// </summary>
public sealed class OrphanedClientSweepHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OrphanedClientSweepHostedService> logger) : BackgroundService
{
    /// <summary>How often the worker wakes up to sweep, after the startup pass (spec 320 §4.4).</summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // StartAsync must never wait on Keycloak (spec 317's lesson): this
        // yield is what lets BackgroundService.StartAsync return before the
        // pass below ever calls out.
        await Task.Yield();

        using PeriodicTimer timer = new(TickInterval, timeProvider);
        try
        {
            await RunOnceSafelyAsync(stoppingToken); // the startup pass
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// A failed pass must not be a failed host — the sweep is a background
    /// reconciliation nobody is waiting on, and the next tick tries again.
    /// <see cref="BackgroundService"/>'s default
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c> must not apply to
    /// this janitor.
    /// </summary>
    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.OrphanedClientSweepFailed(exception);
        }
    }

    /// <summary>
    /// Sweeps once, through a scope. Public, as
    /// <c>IdempotencyReservationSweepHostedService.RunOnceAsync</c> is, so
    /// tests can drive one pass directly rather than waiting for a timer
    /// tick.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        OrphanedClientSweep sweep = scope.ServiceProvider.GetRequiredService<OrphanedClientSweep>();

        await sweep.SweepAsync(cancellationToken);
    }
}
