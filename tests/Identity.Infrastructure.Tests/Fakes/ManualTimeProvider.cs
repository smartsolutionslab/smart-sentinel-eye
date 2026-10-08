namespace SmartSentinelEye.Identity.Infrastructure.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock (ADR-0054 — no mocking framework, and
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>'s <c>FakeTimeProvider</c>
/// is deliberately not referenced anywhere in this repository).
///
/// <para>
/// Spec 317 (#2170): <c>KioskPrivilegeSweepHostedService</c>'s bound is built
/// from <c>new CancellationTokenSource(TimeSpan, TimeProvider)</c>, so a test
/// must be able to drive that provider's timer without sleeping in real time.
/// Minimal copy of <c>tests/ScenarioSimulator.Tests/Fakes/ManualTimeProvider.cs</c>
/// — not shared, because test projects do not reference each other here.
/// </para>
///
/// <para>
/// <b>Only what the bound actually needs.</b> <see cref="GetUtcNow"/> is a
/// plain field read; <see cref="CreateTimer"/> records a one-shot due time
/// (the bound never asks for a recurring <c>period</c>) and
/// <see cref="Advance"/> moves the clock forward and fires, synchronously and
/// in due-time order, every timer whose time has come — which is what
/// resumes a <see cref="CancellationTokenSource"/> built against this
/// provider.
/// </para>
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ScheduledTimer> timers = [];
    private DateTimeOffset now = start;

    // Completed and replaced on every CreateTimer call, so a waiter blocked on
    // its .Task wakes as soon as one more timer registers — event-driven, not
    // polled. See WaitForPendingTimersAsync.
    private TaskCompletionSource timerRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate)
        {
            return now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ScheduledTimer timer = new(callback, state);
        TaskCompletionSource signal;

        lock (gate)
        {
            timer.DueAt = now + dueTime;
            timers.Add(timer);
            signal = timerRegistered;
            timerRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
        return timer;
    }

    /// <summary>Timers created but not yet fired or disposed — i.e. a consumer currently awaiting one.</summary>
    public int PendingTimerCount
    {
        get
        {
            lock (gate)
            {
                return timers.Count(t => !t.Disposed && !t.Fired);
            }
        }
    }

    /// <summary>
    /// Waits until at least <paramref name="count"/> timers are simultaneously
    /// pending, or <paramref name="timeout"/> (real wall-clock time — this
    /// provider's own virtual clock never advances on its own) elapses.
    ///
    /// <para>
    /// The deterministic alternative to sleeping a fixed real-time interval
    /// and hoping the hosted service's <c>StartAsync</c> has reached its
    /// bound's <see cref="CreateTimer"/> call by then.
    /// </para>
    /// </summary>
    public async Task<bool> WaitForPendingTimersAsync(int count, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (true)
        {
            Task signal;

            lock (gate)
            {
                if (timers.Count(t => !t.Disposed && !t.Fired) >= count)
                {
                    return true;
                }

                signal = timerRegistered.Task;
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.WhenAny(signal, Task.Delay(remaining));
        }
    }

    /// <summary>
    /// Moves the clock forward by <paramref name="delta"/> and fires every
    /// timer now due, earliest first. Firing runs the callback synchronously
    /// on the calling (test) thread.
    /// </summary>
    public void Advance(TimeSpan delta)
    {
        List<ScheduledTimer> due;

        lock (gate)
        {
            now += delta;
            due = [.. timers.Where(t => !t.Disposed && !t.Fired && t.DueAt <= now).OrderBy(t => t.DueAt)];
        }

        foreach (ScheduledTimer timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ScheduledTimer(TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset DueAt { get; set; }

        public bool Disposed { get; private set; }

        public bool Fired { get; private set; }

        public void Fire()
        {
            if (Fired || Disposed)
            {
                return;
            }

            Fired = true;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
