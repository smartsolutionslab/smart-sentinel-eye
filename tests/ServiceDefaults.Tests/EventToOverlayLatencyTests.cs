using System.Diagnostics.Metrics;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 025 FR-005 / FR-006. Both guards live in one place so a second caller
/// cannot forget either, and both prevent the same class of error: a
/// measurement that makes the budget look better than it is.
/// </summary>
public class EventToOverlayLatencyTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-22T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-006. Fabs step their clocks with PTP, so the end of a leg can precede
    /// its start. That is a stepped clock, not a journey that took negative
    /// time, and recording it would pull a percentile toward zero.
    /// </summary>
    [Fact]
    public void A_negative_duration_is_not_recorded()
    {
        List<double> recorded = Listen();
        EventToOverlayLatency latency = new(new FixedClock(Now));

        latency.RecordEventToOverlayState(Now.AddSeconds(5));

        recorded.ShouldBeEmpty("a clock step is not a fast journey");
    }

    /// <summary>FR-005. Nothing to time is not a time of nothing.</summary>
    [Fact]
    public void An_absent_moment_is_not_recorded()
    {
        List<double> recorded = Listen();
        EventToOverlayLatency latency = new(new FixedClock(Now));

        latency.RecordEventToOverlayState(null);

        recorded.ShouldBeEmpty("an unmeasurable leg must not appear as a 0 ms one");
    }

    /// <summary>The case that must still work, or the guards are just silence.</summary>
    [Fact]
    public void A_real_duration_is_recorded_against_the_whole_leg()
    {
        List<double> recorded = Listen();
        EventToOverlayLatency latency = new(new FixedClock(Now));

        latency.RecordEventToOverlayState(Now.AddMilliseconds(-180));

        recorded.ShouldHaveSingleItem().ShouldBe(180, tolerance: 1);
    }

    /// <summary>
    /// Subscribes to the meter the implementation writes to. Asserting on the
    /// instrument rather than on a mock, because the thing under test is that a
    /// measurement reaches the meter — or does not.
    /// </summary>
    private static List<double> Listen()
    {
        List<double> values = [];
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

        listener.SetMeasurementEventCallback<double>((_, measurement, _, _) => values.Add(measurement));
        listener.Start();

        return values;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
