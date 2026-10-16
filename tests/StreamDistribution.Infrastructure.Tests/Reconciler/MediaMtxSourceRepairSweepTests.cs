using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2960 / spec 378 FR-007. The periodic half of the repair: it must not touch
/// the gateway before Kestrel is listening (ADR-0171), runs every
/// <c>MediaMtxSourceRepair:Interval</c> after that, survives a failed pass, and
/// stops with the host. The pass itself needs a database, so every scope here
/// is a probe that counts creations and throws — the same device
/// <see cref="MediaMtxReconcilerStartupOrderingTests"/> uses.
/// </summary>
public class MediaMtxSourceRepairSweepTests
{
    [Fact]
    public async Task No_pass_starts_before_ApplicationStarted()
    {
        ScopeProbe scopes = new();
        using FakeLifetime lifetime = new();
        using var sweep = Sweep(scopes, lifetime, TimeSpan.FromMilliseconds(20), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        scopes.Created.ShouldBe(0, "ADR-0171: no gateway call may run before the listener is up");
        await sweep.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Passes_repeat_at_the_configured_interval_once_ApplicationStarted_fires()
    {
        ScopeProbe scopes = new();
        using FakeLifetime lifetime = new();
        using var sweep = Sweep(scopes, lifetime, TimeSpan.FromMilliseconds(50), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await scopes.WaitForCreationsAsync(2, TimeSpan.FromSeconds(10));

        scopes.Created.ShouldBeGreaterThanOrEqualTo(2);
        await sweep.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_long_interval_is_honoured_no_pass_runs_before_it_elapses()
    {
        ScopeProbe scopes = new();
        using FakeLifetime lifetime = new();
        using var sweep = Sweep(scopes, lifetime, TimeSpan.FromHours(1), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        scopes.Created.ShouldBe(0, "the startup pass covered t=0; the first sweep is one interval later");
        await sweep.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failing_pass_is_logged_and_the_next_tick_still_runs()
    {
        ScopeProbe scopes = new();
        using FakeLifetime lifetime = new();
        CapturingLogger<MediaMtxSourceRepairSweep> logger = new();
        using var sweep = Sweep(scopes, lifetime, TimeSpan.FromMilliseconds(50), logger);

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await scopes.WaitForCreationsAsync(2, TimeSpan.FromSeconds(10));
        await sweep.StopAsync(CancellationToken.None);

        logger.Entries.ShouldContain(
            entry => entry.Level == LogLevel.Warning
                && entry.Message == "MediaMtxSourceRepairSweep pass failed; the next tick retries."
                && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task The_sweep_stops_when_the_application_is_stopping()
    {
        ScopeProbe scopes = new();
        using FakeLifetime lifetime = new();
        using var sweep = Sweep(scopes, lifetime, TimeSpan.FromMilliseconds(50), new CapturingLogger<MediaMtxSourceRepairSweep>());

        await sweep.StartAsync(CancellationToken.None);
        lifetime.FireApplicationStarted();
        await scopes.WaitForCreationsAsync(1, TimeSpan.FromSeconds(10));

        lifetime.StopApplication();
        await sweep.StopAsync(CancellationToken.None);
        var atStop = scopes.Created;
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        scopes.Created.ShouldBe(atStop, "no pass may start after the sweep was stopped");
    }

    private static MediaMtxSourceRepairSweep Sweep(
        IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime,
        TimeSpan interval,
        ILogger<MediaMtxSourceRepairSweep> logger) =>
        new(
            scopes,
            lifetime,
            Options.Create(new MediaMtxSourceRepairOptions { Interval = interval }),
            TimeProvider.System,
            logger);

    private sealed class ScopeProbe : IServiceScopeFactory
    {
        private int created;

        public int Created => Volatile.Read(ref created);

        public async Task WaitForCreationsAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Created < count)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"expected {count} scope creations, saw {Created}");
                }

                await Task.Delay(10);
            }
        }

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref created);
            throw new InvalidOperationException("probe: the pass reached the scope factory");
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
