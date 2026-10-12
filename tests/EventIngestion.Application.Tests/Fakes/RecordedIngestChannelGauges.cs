using System.Diagnostics.Metrics;
using SmartSentinelEye.EventIngestion.Application.Ingress;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Captures what the two ADR-0175 follow-up gauges (#2863) report at the
/// moment they are polled — the channel-depth gauge
/// (<see cref="IngestChannelSaturation.DepthMetricName"/>) and the
/// time-since-last-write gauge
/// (<see cref="IngestChannelSaturation.TimeSinceLastWriteMetricName"/>), both
/// registered for one specific <see cref="BoundedIngestChannel"/> via
/// <see cref="IngestChannelSaturation.RegisterGauges"/> — by subscribing to
/// the meter the implementation publishes them on rather than to a mock, the
/// same reasoning as <see cref="RecordedIngestChannelSaturation"/>.
///
/// <para>
/// An <c>ObservableGauge</c> is pull-based: nothing is captured until
/// <see cref="Poll"/> asks the listener to collect, which is the moment the
/// production callback actually runs. Disposing this instance disposes the
/// registration it wraps, so one test's gauges cannot keep reporting into the
/// next test's readings on the shared, process-wide meter.
/// </para>
/// </summary>
public sealed class RecordedIngestChannelGauges : IDisposable
{
    private readonly List<int> depthReadings = [];
    private readonly List<double> timeSinceLastWriteReadings = [];
    private readonly MeterListener listener;
    private readonly IDisposable registration;

    public RecordedIngestChannelGauges(BoundedIngestChannel channel)
    {
        registration = IngestChannelSaturation.RegisterGauges(channel);

        listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name != IngestChannelSaturation.MeterName)
                {
                    return;
                }

                if (instrument.Name == IngestChannelSaturation.DepthMetricName
                    || instrument.Name == IngestChannelSaturation.TimeSinceLastWriteMetricName)
                {
                    active.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == IngestChannelSaturation.DepthMetricName)
            {
                depthReadings.Add(measurement);
            }
        });

        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == IngestChannelSaturation.TimeSinceLastWriteMetricName)
            {
                timeSinceLastWriteReadings.Add(measurement);
            }
        });

        listener.Start();
    }

    /// <summary>Polls both gauges once, capturing whatever each reports right now.</summary>
    public void Poll() => listener.RecordObservableInstruments();

    /// <summary>Every depth reading captured since construction, in poll order.</summary>
    public IReadOnlyList<int> DepthReadings => depthReadings;

    /// <summary>Every time-since-last-write reading (seconds) captured since construction, in poll order.</summary>
    public IReadOnlyList<double> TimeSinceLastWriteReadings => timeSinceLastWriteReadings;

    public void Dispose()
    {
        listener.Dispose();
        registration.Dispose();
    }
}
