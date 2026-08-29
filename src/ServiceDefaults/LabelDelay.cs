using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Records how long a kiosk held an overlay label back so it described the same
/// moment as the picture beneath it (spec 046 US2, ADR-0129).
///
/// <para>
/// <b>Deliberately not a <see cref="LatencySegment"/>, for a different reason
/// than <see cref="WallSkew"/>.</b> A skew is not a duration at all. This
/// <em>is</em> a duration — but it is not a duration anything spent
/// <em>travelling</em>. It is a wait the kiosk chose to add. Every segment in
/// <see cref="LatencyBudget"/> answers <em>how long did this take</em>; this one
/// answers <em>how long did we decide to hold</em>, and mixing an intended
/// figure into observed ones means a dashboard's p99 can rise because the
/// mechanism worked.
/// </para>
///
/// <para>
/// The segments enum does carry fragments as well as whole legs
/// (<c>is_whole_leg</c>), so a fragment was the tempting filing. It does not
/// fit: a fragment is part of one of ADR-0015's six legs, and a hold is a
/// fragment of none of them.
/// </para>
///
/// <para>
/// <b>The honest cost, recorded here because nothing else records it.</b> This
/// delay <em>does</em> spend from the 800 ms path — a held label is a later
/// label. Keeping it out of the segment histograms means a reader of the
/// latency dashboards alone will not see that spending. That is the trade, and
/// it is bounded rather than hidden: <see cref="CapMilliseconds"/> is carried as
/// a tag on every observation, and past it the kiosk shows the label
/// immediately instead of holding it.
/// </para>
/// </summary>
public static class LabelDelay
{
    /// <summary>
    /// Registered alongside <see cref="LatencyBudget"/>'s meter in
    /// <c>Extensions.ConfigureOpenTelemetry</c>. A meter nobody registers
    /// records into nothing and raises no error.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.LabelDelay";

    /// <summary>
    /// The longest a label may be held (spec 046 FR-009). Equal to the
    /// presentation-buffer leg's budget and for the same reason: a held label is
    /// a later label, and lateness is what the 800 ms budget bounds.
    ///
    /// <para>
    /// Carried as a tag so a reader can tell a hold from a hold that was refused
    /// without knowing the spec.
    /// </para>
    /// </summary>
    public const double CapMilliseconds = 200;

    /// <summary>
    /// Above this a figure is not describing a hold — a backgrounded tab whose
    /// timers were throttled, or a clock that moved. Mirrors
    /// <see cref="LatencyBudget"/>'s ceiling, and applies for the same reason.
    /// </summary>
    private static readonly TimeSpan AbsurdlyLong = TimeSpan.FromSeconds(60);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> Held = Meter.CreateHistogram<double>(
        name: "sse.overlay.label_delay",
        unit: "ms",
        description: "How long a kiosk held an overlay label back to match the age of its tile's picture.");

    /// <summary>
    /// Records one hold, attributed to the tile that reported it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The achieved hold, never the intended one</b> (spec 046 FR-015). A
    /// timer fires late under load, and reporting the figure that was asked for
    /// would make this instrument agree with itself no matter what the browser
    /// actually did.
    /// </para>
    /// <para>
    /// Attributed per camera for the same reason the latency segments are
    /// (#1931): one blended figure hides the single tile that is out.
    /// </para>
    /// </remarks>
    public static void Record(TimeSpan held, Guid? camera)
    {
        // Negative is impossible from a measured hold, so it can only mean a
        // caller computed it wrongly. Dropped rather than recorded, so a broken
        // caller shows as missing data instead of an impossibly prompt kiosk.
        if (held < TimeSpan.Zero || held > AbsurdlyLong)
        {
            return;
        }

        TagList tags =
        [
            new("cap_ms", CapMilliseconds),
        ];

        if (camera is not null)
        {
            tags.Add("camera", camera.Value.ToString());
        }

        Held.Record(held.TotalMilliseconds, tags);
    }
}
