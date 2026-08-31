using System.Globalization;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// How the audit pipeline's span divides, and what the division cannot reach
/// (spec 053 US1).
///
/// <para>
/// <b>The observed span is partitioned exactly per row, and that is not the
/// achievement.</b> Occurred → handler-entered → received are consecutive, so
/// each row's two parts sum to that row's whole by construction, and a residual
/// there means stamps that disagree rather than time nobody could account for.
/// </para>
///
/// <para>
/// <b>Per row is doing real work in that sentence.</b> The figures reported here
/// are medians, and medians do not add — so the printed parts can fail to sum to
/// the printed total with nothing whatever wrong. Two different questions,
/// answered separately: see <see cref="UnattributedMs"/> and
/// <see cref="PerRowResidualMs"/>.
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
    int RowsMissingStamps,
    double PerRowResidualMs = 0)
{
    /// <summary>
    /// The gap left by the reported figures — <b>median arithmetic, not an
    /// apparatus check</b>.
    ///
    /// <para>
    /// <b>Medians do not add.</b> Per row the two parts are consecutive intervals
    /// covering the whole span exactly, but the median of a sum is not the sum of
    /// medians, so this can be non-zero with a perfectly sound apparatus. It
    /// reads as ~0 here only because <see cref="InHandlerMs"/> is degenerate at
    /// ~0, which makes the medians additive by accident rather than by right.
    /// </para>
    ///
    /// <para>
    /// Use <see cref="PerRowResidualMs"/> to ask whether the stamps are sound.
    /// This one answers a different and weaker question: whether the three
    /// numbers printed above are mutually consistent.
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

    /// <summary>
    /// <b>The real apparatus check</b>: the median of each row's own
    /// <c>total − before − in</c>, computed row by row and only then reduced.
    ///
    /// <para>
    /// Exactly zero by construction when the stamps are sound, because
    /// occurred → handler-entered → received are consecutive. Non-zero means the
    /// stamps genuinely disagree with the timestamps that bracket them — an
    /// out-of-order stamp, a clock stepping mid-run — and unlike
    /// <see cref="UnattributedMs"/> it cannot be produced by the statistics
    /// alone. This is the one worth failing over.
    /// </para>
    /// </summary>
    public bool PartsCoverEveryRow => Math.Abs(PerRowResidualMs) < 0.001;

    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"""
         rows measured                         : {RowsMeasured} ({RowsMissingStamps} missing stamps)
         observed span (occurred → received)   : {TotalMs:F1} ms
           before handler (two processes, one clock): {BeforeHandlerMs:F1} ms
           in handler (one clock, exact)       : {InHandlerMs:F1} ms
           unattributed (median arithmetic)    : {UnattributedMs:F1} ms
           per-row residual (apparatus)        : {PerRowResidualMs:F3} ms
         write (two clocks — see standing)     : {WriteMs:F1} ms
         requirement span (handover → commit)  : between {RequirementSpanFloorMs:F1} and {RequirementSpanCeilingMs:F1} ms
           width, for want of a publisher stamp: {RequirementSpanWidthMs:F1} ms
           front overhang, outside it          : {FrontOverhangMs:F1} ms
           back shortfall, missed by it        : {BackShortfallMs:F1} ms
         """);
}
