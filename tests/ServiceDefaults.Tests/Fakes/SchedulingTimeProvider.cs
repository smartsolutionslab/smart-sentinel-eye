namespace SmartSentinelEye.ServiceDefaults.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock that drives timers (ADR-0052/0054: <c>FakeTimeProvider</c> is deliberately not
/// referenced anywhere in this repository). A copy of
/// <c>tests/StreamDistribution.Infrastructure.Tests/Fakes/SchedulingTimeProvider.cs</c>, because test projects do
/// not reference one another, with one difference a <see cref="CancellationTokenSource"/> on a custom provider
/// needs: <see cref="ITimer.Change"/> reschedules (<c>CancelAfter</c> arms its timer through it).
/// <see cref="Advance"/> moves the clock and fires, on the calling thread and earliest first, every timer that has
/// come due. Every timer is one-shot: the <c>period</c> is ignored, because the only periodic timer the pipeline
/// creates is the <c>HttpClientFactory</c> handler-expiry sweep (10 s), which these facts do not depend on. A
/// callback observes <see cref="GetUtcNow"/> at the <see cref="Advance"/> target, not at its own due time. A
/// negative due time is refused; the zero due time <see cref="System.Threading.Timer"/> would fire inline is
/// not special-cased, because nothing here creates one (verified by probing the facts).
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
        ScheduledTimer timer = new(this, callback, state);

        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);

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
                    .Where(timer => timer.IsArmed && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();

                if (due is null)
                {
                    return;
                }

                due.IsArmed = false;
            }

            due.InvokeCallback();
        }
    }

    private sealed class ScheduledTimer(SchedulingTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset DueAt { get; private set; }

        public bool IsArmed { get; set; }

        private bool Disposed { get; set; }

        public void InvokeCallback() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "A negative due time other than Infinite is not simulated.");
            }

            lock (owner.gate)
            {
                if (Disposed)
                {
                    return false;
                }

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    IsArmed = false;
                }
                else
                {
                    DueAt = owner.now + dueTime;
                    IsArmed = true;
                }
            }

            return true;
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                Disposed = true;
                IsArmed = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return ValueTask.CompletedTask;
        }
    }
}
