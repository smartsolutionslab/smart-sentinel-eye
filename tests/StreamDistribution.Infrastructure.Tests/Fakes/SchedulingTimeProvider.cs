namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock that can drive timers (ADR-0052/0054:
/// <c>FakeTimeProvider</c> is deliberately not referenced anywhere in this
/// repository). Minimal copy of the timer support in
/// <c>tests/Identity.Infrastructure.Tests/Fakes/ManualTimeProvider.cs</c>: test
/// projects do not reference one another. The existing
/// <c>Fakes/ManualTimeProvider.cs</c> of this project deliberately has no timers
/// and other tests rely on that, so this is a separate type.
///
/// <para>
/// Spec 418: the gateway's whole-read deadline is a
/// <c>new CancellationTokenSource(TimeSpan, TimeProvider)</c>, so a test drives
/// the deadline by <see cref="Advance"/>, which moves the clock and fires, on the
/// calling thread and earliest first, every one-shot timer that has come due.
/// </para>
/// </summary>
internal sealed class SchedulingTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly object gate = new();
    private readonly List<ScheduledTimer> timers = [];
    private DateTimeOffset now = start;

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

        lock (gate)
        {
            timer.DueAt = now + dueTime;
            timers.Add(timer);
        }

        return timer;
    }

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
                    .Where(timer => !timer.Disposed && !timer.Fired && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
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
