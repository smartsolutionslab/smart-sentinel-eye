using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Captures what the handler reported to the latency budget, so a test can
/// assert the leg was measured — and, more importantly, that it was **not**
/// measured when there is nothing to measure (spec 025 FR-005).
/// </summary>
public sealed class RecordingLatencyBudget(IClock clock) : ILatencyBudget
{
    /// <summary>
    /// For the cases that only care what was reported. The clock it reads is
    /// fixed, so <see cref="ObservedAt"/> is meaningless for these and the test
    /// does not consult it.
    /// </summary>
    public RecordingLatencyBudget()
        : this(new FakeClock(DateTimeOffset.UnixEpoch))
    {
    }

    public List<DateTimeOffset?> Recorded { get; } = [];

    /// <summary>
    /// When the handler reported, by the supplied clock. The span the
    /// instrument covers is <c>ObservedAt - Recorded</c>, computed by the test:
    /// this fake deliberately does not do that subtraction, because a fake that
    /// re-implements the production calculation proves nothing about it
    /// (spec 133 T002).
    /// </summary>
    public List<DateTimeOffset> ObservedAt { get; } = [];

    public void RecordEventToOverlayState(DateTimeOffset? rootIngestedAt)
    {
        Recorded.Add(rootIngestedAt);
        ObservedAt.Add(clock.UtcNow);
    }
}
