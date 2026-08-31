using System.Globalization;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// How the audit pipeline's span divides, and what the division cannot reach
/// (spec 053 US1).
///
/// <para>
/// <b>The observed span is partitioned exactly, and that is not the achievement.</b>
/// Occurred → handler-entered → received are consecutive, so those two parts sum
/// to the whole by construction and a remainder there would mean rows with
/// missing stamps rather than time nobody could account for.
/// </para>
///
/// <para>
/// <b>The requirement's span is a different matter, and it is bounded rather
/// than measured.</b> It starts where the broker hands the event over — a moment
/// that falls <i>inside</i> "before handler", between the publisher's own work
/// and the wait on the queue. Separating those needs a stamp taken on the
/// publishing side, which this feature deliberately did not add. So what can be
/// stated is a floor and a ceiling, and the distance between them is the honest
/// width of the answer.
/// </para>
/// </summary>
public readonly record struct IngestAttribution(
    double TotalMs,
    double BeforeHandlerMs,
    double InHandlerMs,
    double WriteMs,
    int RowsMeasured,
    int RowsMissingStamps)
{
    /// <summary>
    /// Time inside the observed span that the parts do not account for.
    ///
    /// <para>
    /// Should be ~0: the two parts are consecutive intervals covering the whole
    /// span. Anything else means the stamps disagree with the timestamps that
    /// bracket them, which is a fault in the apparatus rather than a property of
    /// the pipeline — and worth failing over rather than absorbing.
    /// </para>
    /// </summary>
    public double UnattributedMs => TotalMs - (BeforeHandlerMs + InHandlerMs);

    /// <summary>The share of the observed span the named parts explain.</summary>
    public double AttributedFraction =>
        TotalMs <= 0 ? 0 : (BeforeHandlerMs + InHandlerMs) / TotalMs;

    /// <summary>
    /// <b>The least the requirement's span can be</b> — from handler entry,
    /// which is after the broker handed over, to the row being committed.
    /// </summary>
    public double RequirementSpanFloorMs => InHandlerMs + WriteMs;

    /// <summary>
    /// <b>The most it can be</b> — as if the broker handed over the instant the
    /// change occurred, so the whole of "before handler" counts against it.
    /// </summary>
    public double RequirementSpanCeilingMs => BeforeHandlerMs + InHandlerMs + WriteMs;

    /// <summary>
    /// How wide the answer is. **This is the cost of not having a publisher-side
    /// stamp**, stated as a number rather than as a caveat.
    /// </summary>
    public double RequirementSpanWidthMs => RequirementSpanCeilingMs - RequirementSpanFloorMs;

    /// <summary>
    /// What the historic figure includes that the requirement does not: the
    /// publisher's own work, somewhere inside this.
    /// </summary>
    public double FrontOverhangMs => BeforeHandlerMs;

    /// <summary>
    /// What the requirement includes that the historic figure does not: the
    /// write, which happens after the timestamp that figure ends at.
    /// </summary>
    public double BackShortfallMs => WriteMs;

    /// <summary>Rows that arrived without the stamps this depends on.</summary>
    public bool EveryRowStamped => RowsMissingStamps == 0;

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"""
         rows measured                         : {RowsMeasured} ({RowsMissingStamps} missing stamps)
         observed span (occurred → received)   : {TotalMs:F1} ms
           before handler (crosses two clocks) : {BeforeHandlerMs:F1} ms
           in handler (one clock, exact)       : {InHandlerMs:F1} ms
           unattributed                        : {UnattributedMs:F1} ms
         write (after the observed span ends)  : {WriteMs:F1} ms
         requirement span (handover → commit)  : between {RequirementSpanFloorMs:F1} and {RequirementSpanCeilingMs:F1} ms
           width, for want of a publisher stamp: {RequirementSpanWidthMs:F1} ms
           front overhang, outside it          : {FrontOverhangMs:F1} ms
           back shortfall, missed by it        : {BackShortfallMs:F1} ms
         """);
}
