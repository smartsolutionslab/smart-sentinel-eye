using System.Diagnostics;
using System.Diagnostics.Metrics;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Records how far apart the tiles of one kiosk wall are (spec 045 US2,
/// ADR-0128).
///
/// <para>
/// <b>Deliberately not a <see cref="LatencySegment"/>, and this is the point of
/// the file.</b> <see cref="LatencyBudget"/> answers <em>how long did this
/// segment take</em> — a duration some frame actually spent travelling. A skew
/// is neither: it is the <b>spread between two tiles</b>, and no frame ever
/// took it.
/// </para>
///
/// <para>
/// Reusing the segment histogram would have been easy, would have compiled, and
/// would have passed every test — while filing a spread under a name that means
/// a journey. That is exactly the mislabelling this codebase has been caught by
/// before, and it is why <c>segment.is_whole_leg</c>, §IV's <em>"in part"</em>
/// and <em>"recorded, not yet readable"</em> all exist. A separate instrument
/// costs a few lines and makes the confusion impossible.
/// </para>
///
/// <para>
/// <b>No budget tag, because a skew has no leg.</b> The bound it is read
/// against — 33 ms, one frame at the 30 Hz floor ADR-0123 requires of a kiosk —
/// is a property of the wall, not of the 800 ms path. A <c>leg.budget_ms</c>
/// here would invite a dashboard to subtract it from the SLO twice.
/// </para>
/// </summary>
public static class WallSkew
{
    /// <summary>
    /// Registered alongside <see cref="LatencyBudget"/>'s meter in
    /// <c>Extensions.ConfigureOpenTelemetry</c>. A meter nobody registers
    /// records into nothing and raises no error.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.WallSkew";

    /// <summary>
    /// The bound a wall holds its tiles within (spec 045 FR-002). Carried as a
    /// tag so a reader can tell a pass from a breach without knowing the spec.
    ///
    /// <para>
    /// <b>Not ADR-014's <c>&lt; 5 ms</c></b>, which is an inter-display target
    /// for PTP-synced hardware and out of scope (ADR-0128). Confusing the two
    /// sets a sub-frame target no browser can demonstrate.
    /// </para>
    /// </summary>
    public const double BoundMilliseconds = 33;

    /// <summary>
    /// Above this a figure is not describing a wall — a backgrounded tab whose
    /// timers were throttled, or a clock that moved. Mirrors
    /// <see cref="LatencyBudget"/>'s ceiling, and applies for the same reason.
    /// </summary>
    private static readonly TimeSpan AbsurdlyLong = TimeSpan.FromSeconds(60);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> Spread = Meter.CreateHistogram<double>(
        name: "sse.wall.skew",
        unit: "ms",
        description: "Spread between the most- and least-lagged held tile of one kiosk wall.");

    /// <summary>
    /// Records one observation of a wall's spread, attributed to the tile that
    /// reported it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attributed per camera for the same reason the latency segments are
    /// (#1931): a wall reporting one blended figure hides the single tile that
    /// is out, which is most of the reason to measure at all.
    /// </para>
    /// </remarks>
    public static void Record(TimeSpan spread, Guid? camera)
    {
        // Negative is impossible from a max-minus-min, so it can only mean a
        // caller computed it wrongly. Dropped rather than recorded, so a broken
        // caller shows as missing data instead of an impossibly good wall.
        if (spread < TimeSpan.Zero || spread > AbsurdlyLong)
        {
            return;
        }

        TagList tags =
        [
            new("bound_ms", BoundMilliseconds),
        ];

        if (camera is not null)
        {
            tags.Add("camera", camera.Value.ToString());
        }

        Spread.Record(spread.TotalMilliseconds, tags);
    }
}
