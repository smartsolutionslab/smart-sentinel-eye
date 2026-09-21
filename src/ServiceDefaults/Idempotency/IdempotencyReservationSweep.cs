using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// Configuration for <see cref="IdempotencyReservationSweepHostedService{TDbContext}"/>.
/// </summary>
public sealed class IdempotencyReservationSweepOptions
{
    public const string SectionName = "Idempotency:ReservationSweep";

    /// <summary>How often the worker wakes up to sweep. Default hourly.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Deletes unfinished idempotency reservations older than
/// <see cref="IdempotencyReclamation.StaleAfter"/> (#2290 US3), so a key nobody
/// ever retries does not occupy its row forever.
///
/// <para>
/// US1's inline reclaim already heals every <i>retried</i> wedge; this covers
/// the one case that cannot reach: a key that is never presented again. It is
/// what finally gives <c>ix_idempotency_key_reserved_at</c> a reader — the
/// zero-reader signal that found this defect in the first place.
/// </para>
/// </summary>
public sealed class IdempotencyReservationSweepHostedService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<IdempotencyReservationSweepOptions> options,
    ILogger<IdempotencyReservationSweepHostedService<TDbContext>>? logger = null)
    : BackgroundService
    where TDbContext : DbContext
{
    // Same two guards as BeginAsync's reclaim (IdempotencyStore.cs), and the
    // bound is the same constant so the two can never disagree.
    //   - resource_identifier IS NULL: the ADR-0142 §Consequences boundary —
    //     a completed key is never swept, however old.
    //   - reserved_at < NOW() - StaleAfter: the liveness test.
    private const string DeleteStaleReservationsSql =
        """
        DELETE FROM idempotency_key
        WHERE resource_identifier IS NULL
          AND reserved_at < NOW() - {0};
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.Value.TickInterval, timeProvider);
        try
        {
            // Run once at startup so a restart catches up immediately.
            await RunOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// Sweeps once. Public so tests can drive it directly rather than waiting
    /// for a timer tick.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        Ensure.That(scopeFactory).IsNotNull();
        Ensure.That(timeProvider).IsNotNull();
        Ensure.That(options).IsNotNull();

        // This hosted service is a singleton and TDbContext is scoped, so the
        // context is resolved inside its own scope rather than injected into
        // the constructor (the mistake AuditRetentionHostedService.cs:70-73
        // documents avoiding).
        using IServiceScope scope = scopeFactory.CreateScope();
        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        int swept = await dbContext.Database.ExecuteSqlRawAsync(
            DeleteStaleReservationsSql, [IdempotencyReclamation.StaleAfter], cancellationToken);

        // Only when non-zero: a swept reservation means an attempt died
        // somewhere, worth a line an operator can grep for. Hourly x seven
        // services x a chatty zero-count line would buy nothing.
        if (swept > 0)
        {
            logger?.SweptStaleIdempotencyReservations(swept);
        }
    }
}

/// <summary>
/// Registers <see cref="IdempotencyReservationSweepHostedService{TDbContext}"/>
/// alongside a context's <c>IIdempotencyStore</c> registration.
/// </summary>
public static class IdempotencyReservationSweepServiceCollectionExtensions
{
    public static IServiceCollection AddIdempotencyReservationSweep<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        Ensure.That(services).IsNotNull();

        services.AddOptions<IdempotencyReservationSweepOptions>()
            .BindConfiguration(IdempotencyReservationSweepOptions.SectionName);
        services.AddHostedService<IdempotencyReservationSweepHostedService<TDbContext>>();

        return services;
    }
}
