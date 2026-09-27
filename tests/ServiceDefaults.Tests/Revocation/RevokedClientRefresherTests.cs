using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.ServiceDefaults.Revocation;
using SmartSentinelEye.ServiceDefaults.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Tests.Revocation;

/// <summary>
/// Spec 270 (ADR-0160), plan.md §3/§4.3 — drives
/// <c>RevokedClientRefresher.RefreshOnceAsync</c> directly, the internal seam
/// plan.md names so tests do not wait out the real 5-second
/// <c>PeriodicTimer</c>. Uses a hand-written <see cref="AdvanceableClock"/>
/// rather than <c>FakeTimeProvider</c> — the same choice, for the same reason,
/// as <c>ClientCredentialsTokenProviderTests.cs</c>'s own
/// <c>AdvanceableClock</c> (ADR-0054): the only member under test is
/// <see cref="TimeProvider.GetUtcNow"/>.
///
/// <para>
/// <b>Red today: compile.</b> None of <c>RevokedClientRefresher</c>,
/// <c>RevokedClientRegistry</c>, <c>IRevokedClientSource</c> or
/// <c>RevokedClientSnapshotHealthCheck</c> exists on <c>develop</c>.
/// </para>
///
/// <para>
/// <b>Judgment calls beyond plan.md's text</b>, recorded so the implementing
/// engineer can see where this file committed to a shape plan.md left open.
/// Plan.md §4.3 gives <c>IRevokedClientRegistry</c> only one member,
/// <c>Refuses</c>, so the "before first load" / "stale after 6 missed
/// periods" state the health check needs cannot live behind that interface.
/// This file puts it on the concrete <c>RevokedClientRefresher</c> instead —
/// a nullable <c>LastSuccessAt</c> and a <c>public static readonly
/// RefreshPeriod</c> — since the refresher is the one thing that knows when
/// its last success was, and constructs <c>RevokedClientSnapshotHealthCheck</c>
/// over the refresher plus a <see cref="TimeProvider"/> rather than over the
/// registry.
/// </para>
/// </summary>
public class RevokedClientRefresherTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_registry_admits_everything_before_the_first_load()
    {
        RevokedClientRegistry registry = new();

        registry.Refuses("kiosk-269", Start).ShouldBeFalse(
            "before any snapshot has loaded the check must admit — today's behaviour (FR-005) — "
            + "rather than refuse blind.");
    }

    [Fact]
    public async Task A_successful_load_swaps_the_snapshot()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new(("kiosk-269", Start.AddMinutes(-1)));
        RevokedClientRefresher refresher = new(source, registry, clock, NullLogger<RevokedClientRefresher>.Instance);

        await refresher.RefreshOnceAsync(CancellationToken.None);

        registry.Refuses("kiosk-269", Start.AddMinutes(-30)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_throwing_source_keeps_the_previous_snapshot()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new(("kiosk-269", Start.AddMinutes(-1)));
        RevokedClientRefresher refresher = new(source, registry, clock, NullLogger<RevokedClientRefresher>.Instance);
        await refresher.RefreshOnceAsync(CancellationToken.None);

        source.ThrowNext = new InvalidOperationException("Identity unreachable.");
        await refresher.RefreshOnceAsync(CancellationToken.None);

        registry.Refuses("kiosk-269", Start.AddMinutes(-30)).ShouldBeTrue(
            "a failed refresh must keep the previous snapshot rather than clear it (FR-005): "
            + "revocations only accumulate, so the stale snapshot is still correct.");
    }

    [Fact]
    public async Task The_health_check_is_degraded_before_the_first_load()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new();
        RevokedClientRefresher refresher = new(source, registry, clock, NullLogger<RevokedClientRefresher>.Instance);
        RevokedClientSnapshotHealthCheck health = new(refresher, clock);

        HealthCheckResult result = await health.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task The_health_check_is_healthy_once_a_snapshot_has_loaded()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new();
        RevokedClientRefresher refresher = new(source, registry, clock, NullLogger<RevokedClientRefresher>.Instance);
        RevokedClientSnapshotHealthCheck health = new(refresher, clock);
        await refresher.RefreshOnceAsync(CancellationToken.None);

        HealthCheckResult result = await health.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task The_health_check_is_degraded_again_once_six_refresh_periods_pass_without_a_success()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new();
        RevokedClientRefresher refresher = new(source, registry, clock, NullLogger<RevokedClientRefresher>.Instance);
        RevokedClientSnapshotHealthCheck health = new(refresher, clock);
        await refresher.RefreshOnceAsync(CancellationToken.None);

        TimeSpan sixPeriods = TimeSpan.FromTicks(RevokedClientRefresher.RefreshPeriod.Ticks * 6);
        clock.Advance(sixPeriods + TimeSpan.FromSeconds(1));

        HealthCheckResult result = await health.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(
            HealthStatus.Degraded,
            $"the last success is now older than 6 refresh periods ({sixPeriods.TotalSeconds}s); a "
            + "snapshot this stale must report Degraded even though one loaded successfully once.");
    }

    [Fact]
    public async Task One_warning_is_logged_on_the_first_failure_and_none_on_the_second()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new() { ThrowNext = new InvalidOperationException("Identity unreachable.") };
        CapturingLogger<RevokedClientRefresher> logger = new();
        RevokedClientRefresher refresher = new(source, registry, clock, logger);

        await refresher.RefreshOnceAsync(CancellationToken.None);
        source.ThrowNext = new InvalidOperationException("Identity unreachable.");
        await refresher.RefreshOnceAsync(CancellationToken.None);

        logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(
            1, "a transition is logged once, on the first failure, not once per failed attempt "
            + "(plan.md §4.3's Interlocked.Exchange pattern, mirroring WhepAuthValidator).");
    }

    [Fact]
    public async Task One_information_is_logged_on_recovery()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new() { ThrowNext = new InvalidOperationException("Identity unreachable.") };
        CapturingLogger<RevokedClientRefresher> logger = new();
        RevokedClientRefresher refresher = new(source, registry, clock, logger);
        await refresher.RefreshOnceAsync(CancellationToken.None);

        source.Entries = [new RevokedClientEntry("kiosk-269", Start.AddMinutes(-1))];
        await refresher.RefreshOnceAsync(CancellationToken.None);

        logger.Entries.Count(entry => entry.Level == LogLevel.Information).ShouldBe(
            1, "recovery — the first successful load after one or more failures — is logged once.");
    }

    /// <summary>
    /// The loop must not treat a caller-requested shutdown as a fetch failure:
    /// no Warning, no swallowing — the exception propagates so the hosted
    /// service's own loop ends cleanly (plan.md §4.3: "swallows no
    /// OperationCanceledException on shutdown").
    /// </summary>
    [Fact]
    public async Task A_cancellation_from_the_source_propagates_and_is_not_treated_as_a_failure()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        FakeRevokedClientSource source = new() { ThrowNext = new OperationCanceledException(cancellation.Token) };
        CapturingLogger<RevokedClientRefresher> logger = new();
        RevokedClientRefresher refresher = new(source, registry, clock, logger);

        await Should.ThrowAsync<OperationCanceledException>(
            () => refresher.RefreshOnceAsync(cancellation.Token));

        logger.Entries.ShouldBeEmpty("a cancellation is not a fetch failure and must not be logged as one.");
    }

    /// <summary>
    /// Phase-6 review, S2: an <see cref="OperationCanceledException"/> whose
    /// token is not the caller's own — an <c>HttpClient.Timeout</c>, say — is
    /// not this refresher being asked to stop. It must fall through to the
    /// fail-static branch (logged once, snapshot kept) rather than propagate,
    /// because an unconditional rethrow escapes <c>BackgroundService</c> and
    /// stops the whole host on the default
    /// <c>BackgroundServiceExceptionBehavior.StopHost</c> — taking the
    /// revocation loop down with it. Precedent: <c>StreamHealthWatcher.cs</c>,
    /// <c>MqttConnectionLoop.cs</c>.
    /// </summary>
    [Fact]
    public async Task A_cancellation_not_requested_on_the_callers_own_token_is_treated_as_a_failure()
    {
        AdvanceableClock clock = new(Start);
        RevokedClientRegistry registry = new();
        FakeRevokedClientSource source = new(("kiosk-269", Start.AddMinutes(-1)));
        CapturingLogger<RevokedClientRefresher> logger = new();
        RevokedClientRefresher refresher = new(source, registry, clock, logger);
        await refresher.RefreshOnceAsync(CancellationToken.None);

        using CancellationTokenSource unrelated = new();
        await unrelated.CancelAsync();
        source.ThrowNext = new OperationCanceledException(unrelated.Token);

        await refresher.RefreshOnceAsync(CancellationToken.None);

        registry.Refuses("kiosk-269", Start.AddMinutes(-30)).ShouldBeTrue(
            "the previous snapshot must be kept: this cancellation did not come from the caller's "
            + "own token, so it is not a shutdown and must not propagate.");
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning).ShouldBe(
            1, "an unrelated cancellation is a fetch failure like any other and gets the same "
            + "one-Warning-per-transition treatment.");
    }

    /// <summary>
    /// Returns the configured entries, or throws <see cref="ThrowNext"/> once
    /// (and only once) — mirroring <c>ClientCredentialsTokenProviderTests</c>'
    /// <c>MintingHandler.Status</c> toggle, so a test can drive exactly one
    /// failed fetch followed by a recovery.
    /// </summary>
    private sealed class FakeRevokedClientSource : IRevokedClientSource
    {
        public FakeRevokedClientSource(params (string ClientId, DateTimeOffset DisabledAt)[] entries)
        {
            Entries = [.. entries.Select(entry => new RevokedClientEntry(entry.ClientId, entry.DisabledAt))];
        }

        public IReadOnlyList<RevokedClientEntry> Entries { get; set; }

        public Exception? ThrowNext { get; set; }

        public Task<IReadOnlyList<RevokedClientEntry>> FetchAsync(CancellationToken cancellationToken)
        {
            if (ThrowNext is { } exception)
            {
                ThrowNext = null;
                throw exception;
            }

            return Task.FromResult(Entries);
        }
    }

    /// <summary>
    /// Hand-written rather than <c>FakeTimeProvider</c> — ADR-0054, mirroring
    /// <c>ClientCredentialsTokenProviderTests.AdvanceableClock</c> exactly.
    /// </summary>
    private sealed class AdvanceableClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now = now.Add(by);
    }
}
