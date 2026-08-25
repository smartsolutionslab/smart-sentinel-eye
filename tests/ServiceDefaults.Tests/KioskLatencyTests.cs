using System.Diagnostics.Metrics;
using System.Globalization;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 040. The two legs a kiosk can observe, and the guards that keep a
/// figure from flattering the budget it is measured against.
///
/// <para>
/// <b>None of this proves a number came from a frame.</b> CI has no video —
/// <c>camera-sim</c>, <c>scenario-simulator</c> and the ICE host-publishing all
/// sit inside <c>if (isRunMode &amp;&amp; !isE2ETests)</c> — so these tests cover
/// the recording and its guards, and the two figures themselves are read by a
/// person against the run-mode stack. Saying so matters: a green suite standing
/// in for an unexercised claim is the same class of error that produced issue
/// 1714.
/// </para>
/// </summary>
public class KioskLatencyTests
{
    /// <summary>
    /// The case that must work, or every guard below is just silence.
    /// </summary>
    [Fact]
    public void A_real_overlay_draw_is_recorded()
    {
        List<Measurement> recorded = Listen("kiosk-overlay-draw");

        LatencyBudget.Record(LatencySegment.KioskOverlayDraw, TimeSpan.FromMilliseconds(18));

        recorded.ShouldHaveSingleItem().Value.ShouldBe(18, tolerance: 0.5);
    }

    /// <summary>
    /// FR-009, and the case a browser introduces that a service never had.
    /// Browsers throttle backgrounded tabs, so a hidden kiosk reports a figure
    /// describing the throttling rather than the leg.
    /// </summary>
    [Fact]
    public void A_suspended_page_sized_duration_is_not_recorded()
    {
        List<Measurement> recorded = Listen("kiosk-overlay-draw");

        LatencyBudget.Record(LatencySegment.KioskOverlayDraw, TimeSpan.FromMinutes(5));

        recorded.ShouldBeEmpty("five minutes describes a suspended tab, not a 50 ms leg");
    }

    /// <summary>
    /// FR-008, asserted as an <b>absence</b>. A zero would be indistinguishable
    /// from a perfect journey, and would read as a perfect score for one nobody
    /// timed.
    /// </summary>
    [Fact]
    public void A_negative_duration_records_nothing_rather_than_zero()
    {
        List<Measurement> recorded = Listen("kiosk-receive-to-decoded");

        LatencyBudget.Record(LatencySegment.KioskReceiveToDecoded, TimeSpan.FromMilliseconds(-4));

        recorded.ShouldBeEmpty("a clock that moved is not a fast journey — and not a 0 ms one either");
    }

    /// <summary>
    /// FR-007. One combined figure would satisfy any assertion that a number
    /// exists while measuring neither budget, so the two must be tellable apart
    /// by whoever reads them.
    /// </summary>
    [Fact]
    public void The_two_kiosk_legs_are_separable()
    {
        List<Measurement> recorded = Listen("kiosk-overlay-draw", "kiosk-receive-to-decoded");

        LatencyBudget.Record(LatencySegment.KioskOverlayDraw, TimeSpan.FromMilliseconds(20));
        LatencyBudget.Record(LatencySegment.KioskReceiveToDecoded, TimeSpan.FromMilliseconds(40));

        recorded.Count.ShouldBe(2);
        recorded.Select(m => m.Segment).ShouldBe(
            ["kiosk-overlay-draw", "kiosk-receive-to-decoded"],
            ignoreOrder: true);
        recorded.Select(m => m.Leg).ShouldBe(
            ["overlay-composite-render", "sfu-to-kiosk-decode"],
            ignoreOrder: true);
    }

    /// <summary>
    /// <b>The assertion that stops a fragment being read as a leg passing.</b>
    ///
    /// <para>
    /// The decode budget spans SFU-sends → kiosk-decoded, and a browser cannot
    /// see the sending end without a clock shared with the SFU — establishing
    /// one <em>is</em> the presentation-buffer leg, which is not built. So what
    /// is recorded is the cheaper half, and if it were flagged as the whole leg
    /// a dashboard would report a 120 ms budget comfortably met on the strength
    /// of a measurement that excludes the network.
    /// </para>
    ///
    /// <para>
    /// Its name says fragment and its flag says fragment. Both are asserted,
    /// because either alone can be edited without the other noticing.
    /// </para>
    /// </summary>
    [Fact]
    public void The_decode_measurement_does_not_claim_its_leg()
    {
        List<Measurement> recorded = Listen("kiosk-receive-to-decoded");

        LatencyBudget.Record(LatencySegment.KioskReceiveToDecoded, TimeSpan.FromMilliseconds(9));

        Measurement only = recorded.ShouldHaveSingleItem();
        only.IsWholeLeg.ShouldBeFalse(
            "receive-to-decoded excludes transit from the SFU; flagged whole it would report "
            + "the 120 ms budget met on the strength of its cheaper half");
        only.Segment.ShouldNotContain("leg", Case.Insensitive);
        only.Segment.ShouldBe("kiosk-receive-to-decoded");
        only.LegBudget.ShouldBe(120, "the leg's budget still travels, so a reader can see what "
            + "fraction they are looking at rather than being left with a bare number");
    }

    /// <summary>
    /// The overlay leg genuinely is whole — the kiosk sets the state and paints
    /// the result, so nothing is missing and the budget applies directly. Worth
    /// asserting beside the fragment, or the flag reads as decoration.
    /// </summary>
    [Fact]
    public void The_overlay_measurement_does_claim_its_leg()
    {
        List<Measurement> recorded = Listen("kiosk-overlay-draw");

        LatencyBudget.Record(LatencySegment.KioskOverlayDraw, TimeSpan.FromMilliseconds(12));

        Measurement only = recorded.ShouldHaveSingleItem();
        only.IsWholeLeg.ShouldBeTrue();
        only.LegBudget.ShouldBe(50);
    }

    private sealed record Measurement(double Value, string Segment, string Leg, double LegBudget, bool IsWholeLeg);

    /// <summary>
    /// Subscribes to the meter the implementation writes to, keeping the tags —
    /// the leg, its budget and the whole-leg flag are the point here, not just
    /// the number.
    /// </summary>
    private static List<Measurement> Listen(params string[] segments)
    {
        List<Measurement> values = [];
        HashSet<string> wanted = new(segments, StringComparer.Ordinal);
        MeterListener listener = new()
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name == LatencyBudget.MeterName)
                {
                    active.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
        {
            string segment = string.Empty;
            string leg = string.Empty;
            double budget = 0;
            bool whole = false;
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                switch (tag.Key)
                {
                    case "segment": segment = tag.Value?.ToString() ?? string.Empty; break;
                    case "leg": leg = tag.Value?.ToString() ?? string.Empty; break;
                    case "leg.budget_ms": budget = Convert.ToDouble(tag.Value, CultureInfo.InvariantCulture); break;
                    case "segment.is_whole_leg": whole = Convert.ToBoolean(tag.Value, CultureInfo.InvariantCulture); break;
                    default: break;
                }
            }
            // Only this test's own segments. The meter is process-wide and xUnit
            // runs classes in parallel, so an unfiltered listener also collects
            // whatever EventToOverlayLatencyTests is emitting next door -- which
            // made three assertions here fail intermittently before this filter.
            if (wanted.Contains(segment))
            {
                values.Add(new Measurement(measurement, segment, leg, budget, whole));
            }
        });
        listener.Start();

        return values;
    }
}
