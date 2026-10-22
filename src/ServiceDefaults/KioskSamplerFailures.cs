using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Counts the times a kiosk tile's sampler threw (spec 435, ADR-0180).
///
/// <para>
/// <b>Deliberately not a <see cref="LatencySegment"/>.</b> A failure is an
/// event, not a duration, so it rides its own counter. There is no value
/// parameter: the count the kiosk carries is ignored by construction, so it
/// cannot be recorded as a latency figure by mistake.
/// </para>
/// </summary>
public static class KioskSamplerFailures
{
    /// <summary>
    /// Registered in <c>Extensions.ConfigureOpenTelemetry</c>. A meter nobody
    /// registers records into nothing and raises no error.
    /// </summary>
    public const string MeterName = "SmartSentinelEye.KioskSamplerFailures";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        name: "sse.kiosk.sampler_failures",
        unit: "{failure}",
        description: "Sampler failures reported by a kiosk tile.");

    /// <summary>
    /// Counts one failure, attributed to the tile that reported it. The endpoint
    /// guarantees a non-empty camera before calling.
    /// </summary>
    public static void Record(Guid camera) =>
        Failures.Add(1, new TagList { { "camera", camera.ToString() } });
}
