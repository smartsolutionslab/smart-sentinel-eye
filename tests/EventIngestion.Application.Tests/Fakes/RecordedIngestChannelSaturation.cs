using System.Diagnostics.Metrics;
using SmartSentinelEye.EventIngestion.Application.Ingress;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Fakes;

/// <summary>
/// Collects what <see cref="IngestChannelSaturation"/> actually recorded, by
/// subscribing to the meter the implementation writes to rather than to a
/// mock — the same reasoning as <see cref="RecordedIngestVolume"/>.
/// </summary>
public sealed class RecordedIngestChannelSaturation : IDisposable
{
    private readonly List<double> blockedDurationsSeconds = [];
    private readonly MeterListener listener;

    public RecordedIngestChannelSaturation()
    {
        listener = new MeterListener
        {
            InstrumentPublished = (instrument, active) =>
            {
                if (instrument.Meter.Name == IngestChannelSaturation.MeterName
                    && instrument.Name == IngestChannelSaturation.MetricName)
                {
                    active.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<double>(
            (_, measurement, _, _) => blockedDurationsSeconds.Add(measurement));

        listener.Start();
    }

    /// <summary>Every blocked-write duration the instrument emitted, in order, in seconds.</summary>
    public IReadOnlyList<double> BlockedDurationsSeconds => blockedDurationsSeconds;

    public void Dispose() => listener.Dispose();
}
