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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // T001 (spec 320, #2181): declaration only. The three collaborators
        // below are read here so the primary constructor compiles clean of
        // CS9113/S2325 ahead of T012's real implementation (plan §5).
        _ = scopeFactory;
        _ = timeProvider;
        _ = logger;
        throw new NotImplementedException();
    }

    /// <summary>
    /// Sweeps once, through a scope. Public, as
    /// <c>IdempotencyReservationSweepHostedService.RunOnceAsync</c> is, so
    /// tests can drive one pass directly rather than waiting for a timer
    /// tick.
    /// </summary>
    public Task RunOnceAsync(CancellationToken cancellationToken)
    {
        _ = scopeFactory;
        throw new NotImplementedException();
    }
}
