using System.Globalization;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Whether an attribution taken between two processes' clocks may be believed
/// (spec 053 SC-003).
///
/// <para>
/// <b>"We could not tell" is an outcome here, not a failure.</b> If the two
/// clocks might be further apart than the effect being investigated, then the
/// breakdown is not evidence about the pipeline — it is evidence about the
/// clocks. Saying so is the correct result, and this type exists so that
/// saying so is a value the run produces rather than a sentence somebody
/// remembers to write.
/// </para>
/// </summary>
public enum AttributionStanding
{
    /// <summary>The clocks are close enough that the breakdown means what it says.</summary>
    Established,

    /// <summary>
    /// They are not. The breakdown is reported, and reported as untrustworthy.
    /// </summary>
    NotEstablished,
}

/// <summary>
/// The standing of an attribution, and the reason for it.
/// </summary>
public readonly record struct AttributionVerdict(AttributionStanding Standing, string Reason)
{
    /// <summary>
    /// How far apart the clocks may be before an attribution between them stops
    /// meaning anything (spec 053 SC-003).
    ///
    /// <para>
    /// Ten milliseconds against a gap of roughly thirty-five. A skew of that
    /// size would not overturn the finding, but a skew several times larger
    /// could account for a whole part of the breakdown on its own.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Decides on the <b>worst case</b>, not the measured skew.
    ///
    /// <para>
    /// The skew comes with a residual, and ignoring it would let a reading of
    /// "9 ms ± 40 ms" be reported as established — a number well inside the
    /// threshold whose uncertainty swallows the threshold whole. Deciding on
    /// skew alone is the single mutation that would produce a plausible wrong
    /// answer rather than an obviously broken one.
    /// </para>
    /// </summary>
    public static AttributionVerdict For(RelativeSkew skew)
    {
        if (skew.WorstCase <= Threshold)
        {
            return new AttributionVerdict(
                AttributionStanding.Established,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the stamping clocks are within {Threshold.TotalMilliseconds:F0} ms — measured {skew}"));
        }

        return new AttributionVerdict(
            AttributionStanding.NotEstablished,
            string.Create(
                CultureInfo.InvariantCulture,
                $"the stamping clocks may differ by {skew.WorstCase.TotalMilliseconds:F2} ms, "
                + $"more than the {Threshold.TotalMilliseconds:F0} ms an attribution between them can absorb — "
                + $"measured {skew}. The breakdown below describes the clocks as much as the pipeline."));
    }

    public bool IsEstablished => Standing == AttributionStanding.Established;

    public override string ToString() =>
        Standing == AttributionStanding.Established
            ? $"ESTABLISHED: {Reason}"
            : $"NOT ESTABLISHED: {Reason}";
}
