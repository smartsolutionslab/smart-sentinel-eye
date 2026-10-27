namespace SmartSentinelEye.SystemVariables.Infrastructure.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock (ADR-0054 — <c>FakeTimeProvider</c> is
/// deliberately not referenced anywhere in this repository). Spec 465 (#3182):
/// the reverse-index seeder waits between attempts with
/// <c>Task.Delay(delay, timeProvider, ct)</c>, so a test drives the backoff by
/// advancing this clock instead of sleeping.
///
/// <para>
/// Minimal copy of <c>tests/Identity.Application.Tests/Fakes/ManualTimeProvider.cs</c>
/// — not shared, because test projects do not reference each other. Only
/// one-shot timers are modelled; that is all <c>Task.Delay</c> creates.
/// </para>
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ScheduledTimer> timers = [];
    private DateTimeOffset now = start;
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

    /// <summary>Timers created but not yet fired or disposed.</summary>
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
    /// Waits until at least <paramref name="count"/> timers are pending, or
    /// <paramref name="timeout"/> of real wall-clock time passes.
    /// </summary>
    public async Task<bool> WaitForPendingTimersAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

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

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.WhenAny(signal, Task.Delay(remaining));
        }
    }

    /// <summary>Moves the clock forward and fires, earliest first, every timer now due.</summary>
    public void Advance(TimeSpan delta)
    {
        DateTimeOffset target;
        lock (gate)
        {
            now += delta;
            target = now;
        }

        while (true)
        {
            ScheduledTimer? due;
            lock (gate)
            {
                due = timers
                    .Where(t => !t.Disposed && !t.Fired && t.DueAt <= target)
                    .OrderBy(t => t.DueAt)
                    .FirstOrDefault();

                if (due is null)
                {
                    return;
                }

                due.Fired = true;
            }

            due.InvokeCallback();
        }
    }

    private sealed class ScheduledTimer(TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset DueAt { get; set; }

        public bool Disposed { get; set; }

        public bool Fired { get; set; }

        public void InvokeCallback() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
