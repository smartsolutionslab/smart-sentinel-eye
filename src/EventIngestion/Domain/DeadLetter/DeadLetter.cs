using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

/// <summary>
/// A rejected MQTT message captured so operators can post-mortem
/// without a redeploy (spec 006 FR-015). Audit-only — no fan-out.
///
/// <para>
/// Since spec 317 (#2325) also the quarantine row for a declared discovery
/// source's unregistered kind — same table, a reason code and hold state
/// rather than a new aggregate (spec.md §0.1, the user's decision).
/// </para>
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

    /// <summary>The machine-readable reason code (spec 317 FR-001).</summary>
    public DeadLetterReason Reason { get; private set; } = null!;

    /// <summary>
    /// Set for a <see cref="DeadLetterReason.Refused"/> or
    /// <see cref="DeadLetterReason.UnknownEventType"/> row — the envelope
    /// parsed. <c>null</c> for a <see cref="DeadLetterReason.ParseFailure"/>
    /// row, which never reached a parsed envelope (spec 317 FR-002).
    /// </summary>
    public Kind? Kind { get; private set; }

    /// <summary>The hold lifecycle state (spec 317 FR-003).</summary>
    public HoldState State { get; private set; } = null!;

    public RejectedAt RejectedAt { get; private set; } = null!;

    private DeadLetter() { }

    /// <summary>
    /// Captures a rejected or held delivery. <paramref name="fab"/> is the
    /// plant the address established, or <c>null</c> when it established none
    /// — the caller decides, because only the ingress knows which of the two
    /// failure modes it hit. <paramref name="kind"/> is set for a parsed
    /// envelope and <c>null</c> for a parse failure.
    ///
    /// <para>
    /// FR-002's invariant: <see cref="DeadLetterReason.UnknownEventType"/>
    /// requires both <paramref name="fab"/> and <paramref name="kind"/> — a
    /// hold with no fab has nothing to key a promotion on, and by
    /// construction a hold always has a parsed kind.
    /// </para>
    /// </summary>
    public static DeadLetter Capture(
        DeliveryTopic topic,
        FabIdentifier? fab,
        RawPayload rawPayload,
        RejectionReason error,
        DeadLetterReason reason,
        Kind? kind,
        IClock clock)
    {
        Ensure.That(topic).IsNotNull();
        Ensure.That(rawPayload).IsNotNull();
        Ensure.That(error).IsNotNull();
        Ensure.That(reason).IsNotNull();
        Ensure.That(clock).IsNotNull();

        if (reason == DeadLetterReason.UnknownEventType)
        {
            if (fab is null)
            {
                throw new ArgumentException(
                    "A hold for an unknown event type must carry the fab it was held in.", nameof(fab));
            }

            if (kind is null)
            {
                throw new ArgumentException(
                    "A hold for an unknown event type must carry the kind that was unknown.", nameof(kind));
            }
        }

        return new DeadLetter
        {
            Id = DeadLetterIdentifier.New(),
            Topic = topic,
            Fab = fab,
            RawPayload = rawPayload,
            Error = error,
            Reason = reason,
            Kind = kind,
            State = HoldState.Held,
            RejectedAt = RejectedAt.From(clock.UtcNow),
        };
    }
}
