using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378 FR-007, adversarial pass. The sweep under a slow or failing
/// dependency: a pass that outlives its interval, a shutdown that lands
/// mid-pass, a timeout that is not a shutdown. Each pass reaches a real scope
/// whose <see cref="IDbContextFactory{TContext}"/> stands in for the slow
/// dependency, so no database is needed.
/// </summary>
public class MediaMtxSourceRepairSweepAdversarialTests
{
    [Fact]
    public async Task A_pass_longer_than_the_interval_never_overlaps_the_next()
    {
        SlowContextFactory slow = new(TimeSpan.FromMilliseconds(150), () => new InvalidOperationException("probe"));
        using FakeLifetime lifetime = new();
        await using var provider = Provider(slow);
        using var sweep = Sweep(provider, lifetime, TimeSpan.FromMilliseconds(20), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await slow.WaitForEntriesAsync(3, TimeSpan.FromSeconds(10));
        await sweep.StopAsync(CancellationToken.None);

        slow.MaximumInFlight.ShouldBe(1, "PeriodicTimer is sequential: an overrun coalesces ticks, it never runs two passes at once");
    }

    [Fact]
    public async Task A_shutdown_that_lands_mid_pass_ends_the_sweep_promptly_and_is_not_reported_as_a_failure()
    {
        SlowContextFactory slow = new(TimeSpan.FromSeconds(30), () => new InvalidOperationException("probe"));
        using FakeLifetime lifetime = new();
        await using var provider = Provider(slow);
        CapturingLogger<MediaMtxSourceRepairSweep> logger = new();
        using var sweep = Sweep(provider, lifetime, TimeSpan.FromMilliseconds(20), logger);

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await slow.WaitForEntriesAsync(1, TimeSpan.FromSeconds(10));

        var stop = sweep.StopAsync(CancellationToken.None);
        (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)))).ShouldBe(stop, "the pass honours the stopping token");
        await stop;

        sweep.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse();
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_timeout_that_is_not_a_shutdown_is_logged_as_a_failed_pass_and_the_sweep_keeps_going()
    {
        SlowContextFactory slow = new(
            TimeSpan.Zero,
            () => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));
        using FakeLifetime lifetime = new();
        await using var provider = Provider(slow);
        CapturingLogger<MediaMtxSourceRepairSweep> logger = new();
        using var sweep = Sweep(provider, lifetime, TimeSpan.FromMilliseconds(20), logger);

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await slow.WaitForEntriesAsync(3, TimeSpan.FromSeconds(10));
        await sweep.StopAsync(CancellationToken.None);

        sweep.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse();
        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Warning && entry.Exception is TaskCanceledException);
    }

    [Fact]
    public async Task A_dependency_that_fails_at_once_is_retried_once_per_tick_not_in_a_tight_loop()
    {
        SlowContextFactory slow = new(TimeSpan.Zero, () => new InvalidOperationException("connection refused"));
        using FakeLifetime lifetime = new();
        await using var provider = Provider(slow);
        using var sweep = Sweep(provider, lifetime, TimeSpan.FromMilliseconds(200), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await sweep.StopAsync(CancellationToken.None);

        slow.Entries.ShouldBeLessThanOrEqualTo(7, "five 200 ms ticks fit in one second; a failure must not shorten the wait");
    }

    private static ServiceProvider Provider(SlowContextFactory slow) =>
        new ServiceCollection()
            .AddSingleton<IDbContextFactory<StreamDistributionDbContext>>(slow)
            .AddSingleton<IRtspGateway>(new SourceRepairGateway())
            .BuildServiceProvider();

    private static MediaMtxSourceRepairSweep Sweep(
        IServiceProvider provider,
        IHostApplicationLifetime lifetime,
        TimeSpan interval,
        ILogger<MediaMtxSourceRepairSweep> logger) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lifetime,
            Options.Create(new MediaMtxSourceRepairOptions { Interval = interval }),
            TimeProvider.System,
            logger);

    /// <summary>
    /// Stands in for a slow dependency at the start of a pass: holds for
    /// <c>hold</c> (honouring the token), then throws what <c>failure</c> makes.
    /// </summary>
    private sealed class SlowContextFactory(TimeSpan hold, Func<Exception> failure)
        : IDbContextFactory<StreamDistributionDbContext>
    {
        private int entries;
        private int inFlight;
        private int maximumInFlight;

        public int Entries => Volatile.Read(ref entries);

        public int MaximumInFlight => Volatile.Read(ref maximumInFlight);

        public StreamDistributionDbContext CreateDbContext() =>
            throw new NotSupportedException("the sweep creates its context asynchronously");

        public async Task<StreamDistributionDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref entries);
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(now);
            try
            {
                if (hold > TimeSpan.Zero)
                {
                    await Task.Delay(hold, cancellationToken);
                }

                throw failure();
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }

        public async Task WaitForEntriesAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Entries < count)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"expected {count} passes, saw {Entries}");
                }

                await Task.Delay(10);
            }
        }

        private void InterlockedMax(int candidate)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumInFlight);
                if (candidate <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maximumInFlight, candidate, observed) != observed);
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => stopped.Token;

        public void FireApplicationStarted() => started.Cancel();

        public void StopApplication() => stopping.Cancel();

        public void Dispose()
        {
            started.Dispose();
            stopping.Dispose();
            stopped.Dispose();
        }
    }
}
