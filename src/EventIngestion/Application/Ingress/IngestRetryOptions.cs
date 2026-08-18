namespace SmartSentinelEye.EventIngestion.Application.Ingress;

/// <summary>
/// How long the system keeps trying to store an event before recording it as
/// undeliverable and releasing the sender's copy (spec 020 FR-005).
///
/// <para>
/// A stated decision rather than a constant, because the right answer depends
/// on how long a plant's outages last, and because FR-005 requires the bound to
/// be visible rather than buried.
/// </para>
///
/// <para>
/// <b>The window is a duration, not a count of attempts</b>, and that was not
/// the first attempt at it. Five attempts with exponential backoff sounds
/// generous and exhausts in about six seconds — while SC-001 requires surviving
/// a sixty-second interruption. An attempt count describes effort; what the
/// requirement is about is time, so the bound is time.
/// </para>
/// </summary>
public sealed class IngestRetryOptions
{
    public const string SectionName = "EventIngestion:IngestRetry";

    /// <summary>
    /// How long a delivery may keep failing before it is recorded as a dead
    /// letter and released. Comfortably longer than the sixty seconds SC-001
    /// names, because a deploy or a failover can take minutes and losing the
    /// plant's events to it would defeat the feature.
    /// </summary>
    public TimeSpan MaximumRetryWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Delay before the first retry; doubles up to the maximum.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Ceiling on the backoff, so a long outage is retried steadily rather than
    /// at ever-widening intervals that would leave events sitting for minutes
    /// after storage came back.
    /// </summary>
    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromSeconds(10);
}
