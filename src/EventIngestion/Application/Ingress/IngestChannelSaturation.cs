using System.Diagnostics.Metrics;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// How long a <see cref="BoundedIngestChannel.WriteAsync"/> call spent
/// blocked waiting for channel capacity (ADR-0175). <c>CurrentDepth</c>
/// alone cannot tell an operator "saturated and recovering" apart from
/// "saturated and stuck" — both read as a full channel. This instrument is
/// what tells them apart: a channel that is full but draining quickly
/// records short waits. A channel that is stuck does not record growing
/// waits — a measurement only happens once a blocked write completes, so a
/// write that never completes produces no sample at all. The "stuck" case
/// shows up as this series going silent, not as its values climbing.
///
/// <para>
/// Issue #2863 adds the two gauges that close that gap, registered per
/// <see cref="BoundedIngestChannel"/> instance via <see cref="RegisterGauges"/>.
/// The depth gauge (<see cref="DepthMetricName"/>) is what ADR-0175's own text
/// already claimed existed — <c>CurrentDepth</c> "exposed as a gauge" — but
/// never actually did until now. The time-since-last-write gauge
/// (<see cref="TimeSinceLastWriteMetricName"/>) is pull-based and always has a
/// value, so a channel with nothing left to complete still reports a number —
/// one that keeps climbing — instead of going silent.
/// </para>
///
/// <para>
/// Neither gauge answers "stuck" by itself. A quiet feed with nothing to
/// deliver (an idle night shift) produces the exact same climbing
/// time-since-last-write as a genuinely dead persistence loop — the two look
/// identical on that series alone. Tell them apart by reading it together
/// with the depth gauge: climbing time-since-last-write <em>and</em> depth at
/// or near capacity together mean stuck; climbing time-since-last-write with
/// low depth just means quiet.
/// </para>
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

    /// <summary>
    /// How many deliveries are currently queued in one
    /// <see cref="BoundedIngestChannel"/> instance — what ADR-0175 already
    /// described as a gauge over <c>CurrentDepth</c> (#2863).
    /// </summary>
    public const string DepthMetricName = "sse.ingest.channel.depth";

    /// <summary>
    /// Seconds since one <see cref="BoundedIngestChannel"/> instance last
    /// completed a write (#2863) — keeps climbing, rather than going silent,
    /// while the channel is fully stuck. Read together with
    /// <see cref="DepthMetricName"/>: a climbing value on its own is also what
    /// a quiet feed with nothing to deliver looks like.
    /// </summary>
    public const string TimeSinceLastWriteMetricName = "sse.ingest.channel.time_since_last_write";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> BlockedDuration = Meter.CreateHistogram<double>(
        name: MetricName,
        unit: "ms",
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

        BlockedDuration.Record(duration.TotalMilliseconds);
    }

    /// <summary>
    /// Registers the depth and time-since-last-write gauges for one specific
    /// <paramref name="channel"/> instance (#2863). Each registration gets
    /// its own <see cref="Meter"/> — sharing <see cref="MeterName"/>, so both
    /// gauges still publish on the same meter name everything else here uses
    /// — because an <see cref="Instrument"/> has no disposal of its own;
    /// only a <see cref="Meter"/> can be disposed, and disposing <em>this</em>
    /// one removes only this channel's two gauges, leaving the shared
    /// blocked-duration histogram and every other registration untouched.
    /// </summary>
    public static IDisposable RegisterGauges(BoundedIngestChannel channel)
    {
        Ensure.That(channel).IsNotNull();

        // Stub: establishes the API surface IngestChannelGaugesTests compiles
        // against. No instruments are published yet, so every reading those
        // tests expect to observe is absent — the next commit fills this in.
        return new Meter(MeterName);
    }
}
