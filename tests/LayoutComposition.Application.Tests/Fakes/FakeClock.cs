using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

public sealed class FakeClock(DateTimeOffset moment) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = moment;

    /// <summary>
    /// Moves the clock forward. Lets a test put time <em>inside</em> an awaited
    /// call rather than around it, which is what separates a measurement of a
    /// span from a measurement of the call site (spec 133 SC-001).
    /// </summary>
    public void Advance(TimeSpan elapsed) => UtcNow += elapsed;
}
