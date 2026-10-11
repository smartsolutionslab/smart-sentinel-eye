using System.Diagnostics.Metrics;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// How long a <see cref="BoundedIngestChannel.WriteAsync"/> call spent
/// blocked waiting for channel capacity (ADR-0175). <c>CurrentDepth</c>
/// alone cannot tell an operator "saturated and recovering" apart from
/// "saturated and stuck" — both read as a full channel. This instrument is
/// what tells them apart: a channel that is full but draining quickly
/// records short waits; one that is stuck under sustained overload records
/// waits that keep growing.
/// </summary>
public static class IngestChannelSaturation
{
    /// <summary>
    /// Registered by the context's own <c>AddEventIngestionInfrastructure</c>,
    /// alongside <see cref="IngestVolume.MeterName"/>. A meter nobody
    /// registers records into nothing and raises no error.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.IngestChannelSaturation";

    /// <summary>
    /// The instrument's name, in the same <c>sse.ingest.*</c> space
    /// <see cref="IngestVolume.MetricName"/> claims the root of.
    /// </summary>
    public const string MetricName = "sse.ingest.channel.blocked_duration";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> BlockedDuration = Meter.CreateHistogram<double>(
        name: MetricName,
        unit: "s",
        description: "How long a write to the bounded ingest channel was blocked waiting for capacity (ADR-0175).");

    /// <summary>
    /// Records one blocked write's wait duration. Call only when the write
    /// actually waited — a write that found capacity immediately did not
    /// block, and recording a zero would publish a measurement claiming
    /// saturation that never happened, the same reasoning
    /// <see cref="IngestVolume.Record(SmartSentinelEye.EventIngestion.Domain.Event.Source, long)"/>
    /// applies to a zero-count batch.
    /// </summary>
    public static void RecordBlocked(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        BlockedDuration.Record(duration.TotalSeconds);
    }
}
