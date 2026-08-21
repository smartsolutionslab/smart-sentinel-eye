using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Records the <c>event → overlay state</c> leg into <see cref="LatencyBudget"/>
/// (spec 025). Implements the Application-facing abstraction so a handler can
/// report a measurement without referencing infrastructure — the same shape as
/// <c>OutboxEventBus</c> implementing <c>IEventBus</c>.
/// </summary>
public sealed class EventToOverlayLatency(IClock clock) : ILatencyBudget
{
    public void RecordEventToOverlayState(DateTimeOffset? rootIngestedAt)
    {
        // Not measurable rather than instant. An effect with no plant-floor root
        // — a variable set by an operator, a replayed message from before the
        // moment was carried — has no leg to time, and a zero here would be a
        // perfect score for a journey nobody watched (FR-005).
        if (rootIngestedAt is not { } accepted)
        {
            return;
        }

        // LatencyBudget.Record discards a negative elapsed time (FR-006). Left
        // there rather than duplicated here: it guards every segment, not only
        // this one, and a clock that has been stepped is not this leg's problem
        // to understand.
        LatencyBudget.Record(LatencySegment.EventToOverlayState, clock.UtcNow - accepted);
    }
}
