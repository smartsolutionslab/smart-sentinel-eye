using System.Diagnostics.Metrics;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Records how long a named segment of the end-to-end latency budget took, as a
/// distribution (spec 024, #1681).
///
/// <para>
/// The constitution calls the budget sacred and §VII says a leg without a
/// dashboard cannot ship. Six legs shipped. Nothing in this system recorded how
/// long any of them took, and the cost of that is on record: spec 023 found the
/// first event after a cold start taking twelve to fourteen seconds against a
/// 200 ms leg, unnoticed until a test written for another purpose happened to
/// time it.
/// </para>
///
/// <para>
/// <b>A histogram rather than a gauge, because a budget is a claim about the
/// tail.</b> Spec 023 gave one leg trace spans, which answer "where did this
/// event go" — a different question from "is this leg holding", and one that
/// cannot be answered from a single traversal however well traced.
/// </para>
///
/// <para>
/// <b>Every measurement says whether it covers a whole leg.</b> It usually does
/// not: see <see cref="LatencySegment"/>. The flag rides on the measurement
/// rather than living in a document, because a number that quietly means less
/// than its name is how this programme keeps getting caught.
/// </para>
/// </summary>
public static class LatencyBudget
{
    /// <summary>
    /// Registered with the OpenTelemetry meter provider in
    /// <c>Extensions.ConfigureOpenTelemetry</c>. A meter nobody registers
    /// records into nothing and raises no error, which is the same silence as
    /// an unregistered trace source.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.Latency";

    /// <summary>
    /// Above this, a measurement is describing something other than a journey
    /// — a suspended tab, a stopped debugger, a clock that moved. Three hundred
    /// times the largest leg budget, so it can only ever catch the absurd.
    /// </summary>
    private static readonly TimeSpan AbsurdlyLong = TimeSpan.FromSeconds(60);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> Elapsed = Meter.CreateHistogram<double>(
        name: "sse.latency.segment.duration",
        unit: "ms",
        description: "Time taken by one named segment of the end-to-end latency budget.");

    /// <summary>
    /// Records one traversal. Tags carry the leg and its budget so a reader can
    /// tell a pass from a breach without knowing the constitution (FR-003), and
    /// carry <c>segment.is_whole_leg</c> so nobody reads a fragment as the leg.
    /// </summary>
    public static void Record(LatencySegment segment, TimeSpan elapsed)
    {
        Ensure.That(segment).IsNotNull();

        // Negative elapsed is possible and must not be recorded as fast: legs
        // that span two machines compare two clocks, fabs step theirs with PTP,
        // and a stepped clock can put the end before the start (FR-010).
        if (elapsed < TimeSpan.Zero)
        {
            return;
        }

        // Absurdly long is not a slow journey either (spec 040). Browsers
        // throttle backgrounded tabs, so a kiosk whose page was hidden for a
        // minute reports a figure describing the throttling rather than the
        // leg. Not a budget check — the slowest leg here is budgeted at 200 ms,
        // so a ceiling three hundred times that only catches measurements that
        // cannot be describing a journey at all.
        if (elapsed > AbsurdlyLong)
        {
            return;
        }

        Elapsed.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object>("segment", segment.Name),
            new KeyValuePair<string, object>("leg", segment.Leg),
            new KeyValuePair<string, object>("leg.budget_ms", segment.LegBudgetMilliseconds),
            new KeyValuePair<string, object>("segment.is_whole_leg", segment.IsWholeLeg));
    }
}

/// <summary>
/// A named span of time that some part of the budget covers, and whether it
/// covers all of it.
///
/// <para>
/// <b>Why segments and not legs.</b> ADR-0015 defines
/// <c>event → overlay state</c> as RabbitMQ + projection: from an event being
/// accepted to its effect being applied. No service sees both ends. The
/// ingestion timestamp travels on <c>FabEventIngestedV1</c> and stops there —
/// Automation mints fresh metadata when it publishes downstream, so the service
/// that applies the effect cannot know when the event arrived.
/// </para>
///
/// <para>
/// Measuring the whole leg therefore needs a timestamp propagated through the
/// chain, which is a contract change. Spec 024 raises that rather than making
/// it quietly, and records what can be measured today with
/// <see cref="IsWholeLeg"/> set to <c>false</c>.
/// </para>
/// </summary>
public sealed record LatencySegment
{
    private LatencySegment(string name, string leg, double legBudgetMilliseconds, bool isWholeLeg)
    {
        Name = name;
        Leg = leg;
        LegBudgetMilliseconds = legBudgetMilliseconds;
        IsWholeLeg = isWholeLeg;
    }

    public string Name { get; }

    /// <summary>The ADR-0015 leg this segment falls inside.</summary>
    public string Leg { get; }

    /// <summary>The whole leg's budget, even when this segment is part of it.</summary>
    public double LegBudgetMilliseconds { get; }

    /// <summary>
    /// False when the segment is a fragment. A dashboard comparing a fragment
    /// to the leg's budget is comparing the wrong things, and it should be able
    /// to say so.
    /// </summary>
    public bool IsWholeLeg { get; }

    /// <summary>
    /// The whole `event → overlay state` leg as ADR-0015 defines it: a
    /// plant-floor event being accepted through to its effect being applied.
    ///
    /// <para>
    /// Spec 024 could not record this and defined a fragment instead, which it
    /// deliberately never fed — a fragment reported against the leg's 200 ms
    /// budget would have looked like the leg passing. Spec 025 carried the
    /// acceptance moment downstream, which is what makes the whole leg
    /// recordable, and the fragment is gone rather than left beside it as a
    /// second thing someone could pick by mistake.
    /// </para>
    /// </summary>
    public static readonly LatencySegment EventToOverlayState =
        new("event-to-overlay-state", "event-to-overlay-state", 200, isWholeLeg: true);

    /// <summary>
    /// The whole `overlay composite + render` leg (spec 040, ADR-0015 ≤ 50 ms):
    /// an overlay's state changing through to the browser having painted it.
    ///
    /// <para>
    /// A whole leg, unusually. The kiosk can observe both ends — it sets the
    /// state and it paints the result — so nothing is missing and the budget
    /// applies directly.
    /// </para>
    /// </summary>
    public static readonly LatencySegment KioskOverlayDraw =
        new("kiosk-overlay-draw", "overlay-composite-render", 50, isWholeLeg: true);

    /// <summary>
    /// A <b>fragment</b> of the `SFU → kiosk decode` leg (spec 040, ADR-0015
    /// ≤ 120 ms): the first packet of a frame arriving through to that frame
    /// being decoded.
    ///
    /// <para>
    /// <b>Not the leg, and <see cref="IsWholeLeg"/> says so.</b> The budget
    /// spans <em>SFU sends → kiosk has decoded</em>, and a browser cannot see
    /// the sending end without a clock shared with the SFU. Establishing one
    /// <em>is</em> the presentation-buffer leg, which is not built — so the
    /// statistic that would close this gap depends on the leg whose absence
    /// created the gap.
    /// </para>
    ///
    /// <para>
    /// The available alternatives are worse rather than better.
    /// <c>jitterBufferDelay</c> measures how long frames wait to be played out
    /// — that is the presentation buffer, a <em>different</em> leg, and
    /// recording it here would attribute one leg's time to another.
    /// <c>totalDecodeTime</c> alone is codec work at single-digit milliseconds
    /// and would report magnificently against 120 ms while meaning nothing.
    /// </para>
    ///
    /// <para>
    /// So this records the honest fragment and flags it. Constitution §IV
    /// records the leg as measured <em>in part</em> rather than rounding up
    /// (ADR-0122).
    /// </para>
    /// </summary>
    public static readonly LatencySegment KioskReceiveToDecoded =
        new("kiosk-receive-to-decoded", "sfu-to-kiosk-decode", 120, isWholeLeg: false);
}
