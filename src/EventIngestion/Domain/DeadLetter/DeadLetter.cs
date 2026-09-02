using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// A rejected MQTT message captured so operators can post-mortem
/// without a redeploy (spec 006 FR-015). Audit-only — no fan-out.
/// </summary>
public sealed class DeadLetter : AggregateRoot<DeadLetterIdentifier>
{
    public DeliveryTopic Topic { get; private set; } = null!;

    /// <summary>
    /// The plant the delivery came from, where the delivery address establishes
    /// one (spec 018 FR-008); <c>null</c> where it does not (FR-010).
    ///
    /// <para>
    /// Nullable <b>permanently</b>, unlike spec 016's transitional stream fab: a
    /// malformed address has no plant and FR-010 forbids inventing one, so there
    /// is no follow-up NOT NULL migration to file. The null also does the work —
    /// it satisfies no <c>IN</c>, so such a row reaches nobody (FR-011) without
    /// the listing needing a special case.
    /// </para>
    /// </summary>
    public FabIdentifier? Fab { get; private set; }

    public RawPayload RawPayload { get; private set; } = null!;

    public RejectionReason Error { get; private set; } = null!;

    public DateTimeOffset RejectedAt { get; private set; }

    private DeadLetter() { }

    /// <summary>
    /// Captures a rejected delivery. <paramref name="fab"/> is the plant the
    /// address established, or <c>null</c> when it established none — the
    /// caller decides, because only the ingress knows which of the two failure
    /// modes it hit.
    /// </summary>
    public static DeadLetter Capture(
        DeliveryTopic topic, FabIdentifier? fab, RawPayload rawPayload, RejectionReason error, IClock clock)
    {
        Ensure.That(topic).IsNotNull();
        Ensure.That(rawPayload).IsNotNull();
        Ensure.That(error).IsNotNull();
        Ensure.That(clock).IsNotNull();
        return new DeadLetter
        {
            Id = DeadLetterIdentifier.New(),
            Topic = topic,
            Fab = fab,
            RawPayload = rawPayload,
            Error = error,
            RejectedAt = clock.UtcNow,
        };
    }
}
