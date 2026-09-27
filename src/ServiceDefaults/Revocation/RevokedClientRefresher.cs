using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.ServiceDefaults.Revocation;

/// <summary>
/// Replaces <see cref="RevokedClientRegistry"/>'s snapshot every
/// <see cref="RefreshPeriod"/> from <see cref="IRevokedClientSource"/>
/// (spec 270, ADR-0160 §2). Fail-static (FR-005): a failed fetch keeps the
/// previous snapshot rather than clearing it, because revocations only
/// accumulate and a revoke needs the same Identity the failed fetch could not
/// reach.
///
/// <para>
/// <see cref="RefreshOnceAsync"/> is the test seam plan.md §4.3 names, so unit
/// tests drive one fetch at a time against a hand-written
/// <see cref="TimeProvider"/> rather than waiting out the real
/// <see cref="PeriodicTimer"/>.
/// </para>
///
/// <para>
/// Health-check state — <see cref="LastSuccessAt"/> and
/// <see cref="RefreshPeriod"/> — lives here rather than behind
/// <see cref="IRevokedClientRegistry"/>, whose one member is
/// <c>Refuses</c>: this is the one thing that knows when its last success
/// was, and <see cref="RevokedClientSnapshotHealthCheck"/> is constructed
/// over it directly.
/// </para>
/// </summary>
public sealed class RevokedClientRefresher(
    IRevokedClientSource source,
    RevokedClientRegistry registry,
    TimeProvider clock,
    ILogger<RevokedClientRefresher> logger) : BackgroundService
{
    /// <summary>
    /// A constant, not a configuration knob (plan.md §3): this is a security
    /// check and 5 s has to be short, not tunable.
    /// </summary>
    public static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(5);

    // 0/1 exchange pattern mirrors WhepAuthValidator.realmUnreachable: a
    // transition is worth one log line, not one per attempt, and every
    // service instance runs one of these against a shared Identity.
    private int failing;
    private int loadedOnce;

    // Written on the refresh loop's thread, read on whatever thread the
    // health-check middleware runs on. A DateTimeOffset? auto-property has no
    // memory-barrier guarantee, so a concurrent reader could observe a
    // half-written value; long ticks with Volatile.Read/Write does (phase-6
    // review, N2). -1 is the "never" sentinel: DateTimeOffset.UtcTicks is
    // never negative for any value TimeProvider.GetUtcNow() can produce.
    private const long NeverTicks = -1;
    private long lastSuccessAtUtcTicks = NeverTicks;

    /// <summary>
    /// When a fetch last succeeded, read by <see cref="RevokedClientSnapshotHealthCheck"/>.
    /// <see langword="null"/> before the first success (FR-005).
    /// </summary>
    public DateTimeOffset? LastSuccessAt
    {
        get
        {
            long ticks = Volatile.Read(ref lastSuccessAtUtcTicks);
            return ticks == NeverTicks ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Immediate on start (plan.md §4.3), then every RefreshPeriod.
        await RefreshOnceAsync(stoppingToken);

        using PeriodicTimer timer = new(RefreshPeriod, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshOnceAsync(stoppingToken);
        }
    }

    /// <summary>
    /// One fetch-and-swap attempt. A cancellation of <paramref name="cancellationToken"/>
    /// itself propagates unchanged — it is the caller stopping this service,
    /// not Identity failing, and must not be logged or counted as a fetch
    /// failure. Any other <see cref="OperationCanceledException"/> — an
    /// <c>HttpClient.Timeout</c>, say — is not a shutdown and falls through to
    /// the fail-static branch below (phase-6 review, S2; precedent:
    /// <c>StreamHealthWatcher.cs</c>, <c>MqttConnectionLoop.cs</c>), because an
    /// unconditional rethrow here would escape <see cref="BackgroundService"/>
    /// and stop the whole host on the default
    /// <see cref="Microsoft.Extensions.Hosting.BackgroundServiceExceptionBehavior.StopHost"/>,
    /// taking the revocation loop down with it.
    /// </summary>
    internal async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RevokedClientEntry> entries;
        try
        {
            entries = await source.FetchAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref failing, 1) == 0)
            {
                logger.RevokedClientSnapshotUnavailable(exception);
            }

            return;
        }

        registry.Replace(RevokedClientSnapshot.From(entries));
        Volatile.Write(ref lastSuccessAtUtcTicks, clock.GetUtcNow().UtcTicks);

        bool wasFailing = Interlocked.Exchange(ref failing, 0) == 1;
        if (Interlocked.Exchange(ref loadedOnce, 1) == 0)
        {
            logger.FirstRevokedClientSnapshotLoaded(entries.Count);
        }
        else if (wasFailing)
        {
            logger.RevokedClientSnapshotRestored();
        }
    }
}
