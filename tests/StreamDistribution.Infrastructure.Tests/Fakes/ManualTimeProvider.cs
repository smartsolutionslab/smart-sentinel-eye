namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

/// <summary>
/// A hand-written virtual clock (ADR-0052/0054: <c>FakeTimeProvider</c> is
/// deliberately not referenced anywhere in this repository). Minimal copy of
/// <c>tests/Identity.Infrastructure.Tests/Fakes/ManualTimeProvider.cs</c> without
/// timers: test projects do not reference one another, and nothing here creates one.
///
/// <para>
/// Spec 388 (#2994): the repeat interval of a failing path is measured on the
/// monotonic timestamp, not the wall clock, so a clock step must not move it.
/// <see cref="Advance"/> moves both clocks, as time passing does;
/// <see cref="StepWallClock"/> moves only <see cref="GetUtcNow"/>, as an NTP or
/// operator adjustment does.
/// </para>
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => now;

    public override long GetTimestamp() => timestamp;

    public void Advance(TimeSpan delta)
    {
        now += delta;
        timestamp += delta.Ticks;
    }

    public void StepWallClock(TimeSpan delta) => now += delta;
}
