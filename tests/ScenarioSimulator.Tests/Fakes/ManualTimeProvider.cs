namespace SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock (ADR-0054 — no mocking framework, and
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>'s <c>FakeTimeProvider</c>
/// is deliberately not referenced anywhere in this repository —
/// <c>ServiceDefaults.Tests</c> makes the same choice). Spec 289 / PR-B,
/// T-B07: <c>ClipCueHostedService</c> waits with
/// <c>Task.Delay(delay, TimeProvider, CancellationToken)</c> so a test can
/// drive its scheduling without sleeping in real time.
///
/// <para>
/// <b>Only what <see cref="ClipCueHostedService"/> actually needs.</b>
/// <see cref="GetUtcNow"/> is a plain field read; <see cref="CreateTimer"/>
/// records a one-shot due time (the production code never asks for a
/// recurring <c>period</c>) and <see cref="Advance"/> moves the clock forward
/// and fires, synchronously and in due-time order, every timer whose time has
/// come — which is what resumes a suspended <c>await Task.Delay(...)</c>.
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
    /// and hoping a consumer's background loop(s) have reached their first
    /// <c>Task.Delay(..., this, ...)</c> by then — reliable only when the
    /// thread pool happens to already be warm (e.g. running as part of a
    /// larger suite), and flaky when it isn't (run in isolation, with no
    /// other test having spun up worker threads first). A test with more than
    /// one simultaneously-live consumer loop should await this for the number
    /// of loops it expects to be pending, before its first
    /// <see cref="Advance"/> — not sleep and hope.
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
    /// on the calling (test) thread — the same thread that then typically
    /// polls for the awaiting code's side effect, so there is no cross-thread
    /// race to coordinate.
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
