namespace SmartSentinelEye.Shared.CQRS;

/// <summary>
/// Records how long a leg of the end-to-end latency budget took, from the
/// service that sees its end.
///
/// <para>
/// The abstraction exists for the same reason <see cref="IEventBus"/> does: the
/// Application layer needs the behaviour and must not reference the
/// infrastructure that provides it. `ServiceDefaults` implements this over the
/// meter it already owns.
/// </para>
///
/// <para>
/// <b>Both guards live here, not at the call sites.</b> A leg with no recorded
/// start is not measurable and must record nothing — never a zero, which would
/// read as a perfect score for a journey nobody timed. A negative elapsed time
/// is a stepped clock, not a fast journey; fabs run PTP and the end can precede
/// the start. Putting both in the implementation means a second caller cannot
/// forget either (spec 025 FR-005, FR-006).
/// </para>
/// </summary>
public interface ILatencyBudget
{
    /// <summary>
    /// Records the <c>event → overlay state</c> leg (ADR-0015, ≤ 200 ms): from
    /// the plant-floor event being accepted to its effect being applied.
    /// </summary>
    /// <param name="rootIngestedAt">
    /// When the causing event was accepted, or <see langword="null"/> when this
    /// effect has no plant-floor root — in which case nothing is recorded.
    /// </param>
    void RecordEventToOverlayState(DateTimeOffset? rootIngestedAt);
}
