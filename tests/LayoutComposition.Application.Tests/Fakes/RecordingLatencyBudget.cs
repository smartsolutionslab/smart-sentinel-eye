using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Captures what the handler reported to the latency budget, so a test can
/// assert the leg was measured — and, more importantly, that it was **not**
/// measured when there is nothing to measure (spec 025 FR-005).
/// </summary>
public sealed class RecordingLatencyBudget : ILatencyBudget
{
    public List<DateTimeOffset?> Recorded { get; } = [];

    public void RecordEventToOverlayState(DateTimeOffset? rootIngestedAt) =>
        Recorded.Add(rootIngestedAt);
}
