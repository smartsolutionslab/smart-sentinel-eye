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
    /// Automation asking for a variable value through to that value being
    /// applied. **A fragment of `event → overlay state`**, not the leg: it
    /// starts after ingestion and rule evaluation, and ends before anything
    /// reaches a screen.
    /// </summary>
    public static readonly LatencySegment AutomationToVariableApplied =
        new("automation-to-variable-applied", "event-to-overlay-state", 200, isWholeLeg: false);
}
