namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// The shape of an audit-ingest measurement run (spec 054 US2).
///
/// <para>
/// <b>One definition, read by both runs, so drift is not expressible.</b> The
/// fixture run and the run-mode run exist to be set side by side, and a figure
/// taken with a different generator, rate or event count cannot be compared with
/// one taken this way. Two constants that happen to match would satisfy a reader
/// and nothing else; a single shape means changing it for one run changes it for
/// the other.
/// </para>
///
/// <para>
/// This is the mechanism behind the spec's requirement that every difference
/// between the two runs, other than the environment, is nil or named. The
/// differences here are <b>nil by construction</b>. The ones that remain — which
/// stack, and at what address — are named in
/// <see cref="IngestRunConditions"/>.
/// </para>
/// </summary>
public static class IngestRunShape
{
    /// <summary>
    /// Events published before measurement begins, against their own variable.
    ///
    /// <para>
    /// A separate variable rather than a filter after the fact: the warm-up's
    /// rows would otherwise sit in the same result set as the measured ones with
    /// no way to tell them apart, and the percentiles would include the cold path
    /// the warm-up exists to exclude.
    /// </para>
    /// </summary>
    public const int WarmupEvents = 100;

    /// <summary>The population every percentile is taken over.</summary>
    public const int MeasuredEvents = 1_000;

    /// <summary>
    /// Concurrent writers, one variable each.
    ///
    /// <para>
    /// One variable each is not a detail: the version travels in an
    /// <c>If-Match</c>, so two writers on one variable collide on optimistic
    /// concurrency and generate 409s rather than load.
    /// </para>
    ///
    /// <para>
    /// Fifty, so no writer has to issue faster than every half second to hold the
    /// target rate between them. The pacing sets the rate; the writers only have
    /// to be numerous enough not to become the limit themselves.
    /// </para>
    /// </summary>
    public const int Writers = 50;

    /// <summary>
    /// The rate NFR-001 names. <b>Paced to, not driven past.</b>
    ///
    /// <para>
    /// "Sustained 100 ev/s" is a rate. Driven flat out these same writers reached
    /// 244 ev/s and a 5.5 s span — a faithful measurement of overload, and no
    /// answer at all about the load the requirement describes.
    /// </para>
    /// </summary>
    public const double TargetRatePerSecond = 100;

    /// <summary>
    /// How far the achieved rate may sit from the target before the run is
    /// measuring something else.
    ///
    /// <para>
    /// Below it the pipeline is idle and the breakdown describes a near-idle
    /// stack; above it the run measures overload. Either answers a different
    /// question from the one asked.
    /// </para>
    /// </summary>
    public const double RateTolerance = 0.15;

    /// <summary>Events each writer sends. Exact by construction — see the test.</summary>
    public const int EventsPerWriter = MeasuredEvents / Writers;

    /// <summary>The interval between paced slots, in milliseconds.</summary>
    public const double SlotIntervalMs = 1000d / TargetRatePerSecond;

    /// <summary>The lowest achieved rate a run may report and still be read.</summary>
    public const double MinimumAcceptableRate = TargetRatePerSecond * (1 - RateTolerance);

    /// <summary>The highest.</summary>
    public const double MaximumAcceptableRate = TargetRatePerSecond * (1 + RateTolerance);
}
