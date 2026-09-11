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
    ///
    /// <para>
    /// <b>Call this after the effect has been pushed, never before.</b>
    /// "Applied" means the overlay's new state is on its way to the wall — the
    /// last moment the server owns. Both callers do that: the highlight effect
    /// after <c>OverlayHighlightedAsync</c>, the variable effect after
    /// <c>ResolvedOverlayTextChangedAsync</c>. Anything earlier is a prefix of
    /// the leg reported under the leg's name.
    /// </para>
    ///
    /// <para>
    /// <b>The measurement point moved on 2026-09-11 (#2173), and figures from
    /// either side of that are not comparable.</b> Until then the variable
    /// effect was timed at the value write in SystemVariables, before an outbox
    /// relay, a broker hop and a context boundary; #2072 measured that omitted
    /// remainder at 555 ms and 758 ms server-side. A number that rises by
    /// several hundred milliseconds across that change is the instrument
    /// getting longer, not the system getting slower. Whether the leg holds its
    /// 200 ms budget is #2072's question and is not settled by this.
    /// </para>
    /// </summary>
    /// <param name="rootIngestedAt">
    /// When the causing event was accepted, or <see langword="null"/> when this
    /// effect has no plant-floor root — in which case nothing is recorded.
    /// </param>
    void RecordEventToOverlayState(DateTimeOffset? rootIngestedAt);
}
