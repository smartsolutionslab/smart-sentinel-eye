using System.Diagnostics.CodeAnalysis;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// One normalised row of the audit trail (spec 009 FR-004).
///
/// <para>
/// Not an aggregate root — once written it is immutable. Reads
/// go through query handlers directly against the DbContext to
/// keep the hot search path cheap; writes use the
/// <see cref="IAuditEventRepository"/>'s
/// <see cref="IAuditEventRepository.Add"/> +
/// <see cref="IAuditEventRepository.SaveAsync"/> pair.
/// </para>
///
/// <para>
/// Idempotency comes from the unique index on
/// <see cref="EventIdentifier"/>: Wolverine at-least-once
/// redeliveries hit <c>INSERT ... ON CONFLICT DO NOTHING</c> and
/// produce a single row, not duplicates.
/// </para>
/// </summary>
public sealed class AuditEvent
{
    public AuditEventIdentifier Id { get; private init; }

    public OccurredAt OccurredAt { get; private init; } = null!;

    public ReceivedAt ReceivedAt { get; private init; } = null!;

    public FabIdentifier? Fab { get; private init; }

    public EventKind EventKind { get; private init; } = null!;

    public ResourceKind? ResourceKind { get; private init; }

    public ResourceIdentifier? ResourceIdentifier { get; private init; }

    public ActorIdentifier Actor { get; private init; } = ActorIdentifier.System;

    public ActorUsername? ActorUsername { get; private init; }

    public EventIdentifier EventIdentifier { get; private init; } = null!;

    public AuditPayload Payload { get; private init; } = null!;

    public PayloadSizeBytes PayloadSizeBytes { get; private init; }

    public SchemaVersion SchemaVersion { get; private init; }

    /// <summary>
    /// When this handler was entered, by the consumer's own clock. **Null unless
    /// the measurement switch is on** (spec 053).
    ///
    /// <para>
    /// <b>This exists to divide a span that has only ever been quoted whole.</b>
    /// The pipeline's latency is measured from <see cref="OccurredAt"/> to
    /// <see cref="ReceivedAt"/>, and three decisions have been reasoned from that
    /// single figure without anyone knowing which part of the pipeline spends it.
    /// </para>
    ///
    /// <para>
    /// <b>Its real value is which side of the clock boundary it sits on.</b>
    /// <c>OccurredAt</c> is stamped by the publishing process and everything
    /// after this by the consuming one, so this timestamp is the seam: the span
    /// before it crosses two clocks and carries their disagreement, while the
    /// span after it is measured entirely within one process and is exact.
    /// Without it, that uncertainty is smeared across the whole figure with no
    /// way to say how much.
    /// </para>
    /// </summary>
    public HandlerEnteredAt? HandlerEnteredAt { get; private init; }

    /// <summary>
    /// When the row was <b>inserted</b> — not when its transaction committed —
    /// by the database's clock rather than any process's. Null unless the
    /// measurement switch is on.
    ///
    /// <para>
    /// <b>The distinction is not pedantry: NFR-001's words are "audit row
    /// committed".</b> <c>clock_timestamp()</c> evaluates as the row is written,
    /// inside a transaction that has not yet committed, so the commit itself
    /// falls outside this stamp. Any leg computed from it therefore
    /// <i>under</i>-reports the requirement's back end by whatever the commit
    /// costs. Closing that would need a stamp taken after commit, which is a
    /// second round trip on a path this feature is only supposed to observe.
    /// </para>
    ///
    /// <para>
    /// Written as <c>clock_timestamp()</c> <b>inside the insert statement</b>,
    /// not by a column default. A default was the obvious shape and this table
    /// refuses it: <c>audit_events</c> is a compressed hypertable, and adding a
    /// column with a non-constant default to one fails outright. Either way the
    /// value costs no second write and no round trip, and it is the only
    /// timestamp here taken from the clock every service in this system shares.
    /// </para>
    ///
    /// <para>
    /// It closes the end the historic measurement never covered:
    /// <see cref="ReceivedAt"/> is stamped <i>before</i> the write, so the write
    /// itself has never been in any figure quoted for this pipeline.
    /// </para>
    /// </summary>
    [SuppressMessage(
        "Minor Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification = "Set by the database, not by this code — the repository's insert supplies "
            + "clock_timestamp() for it. Nothing in C# assigns it, which is the point.")]
    public WrittenAt? WrittenAt { get; private init; }

    private AuditEvent() { }

    /// <summary>
    /// Builds a row from the inbound integration event + its
    /// envelope-style metadata. The handler in the Application
    /// layer plugs in the <see cref="V1Mapping"/> derived from
    /// the V1's runtime type; <see cref="IClock"/> stamps
    /// <see cref="ReceivedAt"/> at handler-local time so a
    /// queue backlog is visible as the gap to
    /// <see cref="OccurredAt"/>.
    ///
    /// <para>
    /// <paramref name="handlerEnteredAt"/> is <b>null unless the measurement
    /// switch is on</b> (spec 053), and is the only thing this method does
    /// differently when it is. Passing it does not change the row's meaning —
    /// it divides a span that has only ever been quoted whole.
    /// </para>
    /// </summary>
    public static AuditEvent From(
        V1Envelope envelope, V1Mapping mapping, IClock clock, DateTimeOffset? handlerEnteredAt = null)
    {
        Ensure.That(envelope).IsNotNull();
        Ensure.That(mapping).IsNotNull();
        Ensure.That(clock).IsNotNull();

        return new AuditEvent
        {
            Id = AuditEventIdentifier.New(),
            OccurredAt = OccurredAt.From(envelope.OccurredAt),
            ReceivedAt = ReceivedAt.From(clock.UtcNow),
            HandlerEnteredAt = handlerEnteredAt.HasValue ? HandlerEnteredAt.From(handlerEnteredAt.Value) : null,
            Fab = envelope.Fab.HasValue ? envelope.Fab.Value : null,
            EventKind = EventKind.From(envelope.EventTypeName),
            ResourceKind = mapping.Kind.HasValue ? mapping.Kind.Value : null,
            ResourceIdentifier = mapping.ResourceIdentifier.HasValue ? mapping.ResourceIdentifier.Value : null,
            Actor = envelope.Actor,
            ActorUsername = envelope.ActorUsername.HasValue ? ActorUsername.From(envelope.ActorUsername.Value) : null,
            EventIdentifier = envelope.EventIdentifier,
            Payload = AuditPayload.From(envelope.Payload),
            PayloadSizeBytes = PayloadSizeBytes.Of(envelope.Payload),
            SchemaVersion = SchemaVersion.Current,
        };
    }
}

/// <summary>
/// Inbound-envelope view of a <c>*V1</c> integration event used
/// by <see cref="AuditEvent.From"/>. Decouples the Domain from
/// the Wolverine + serializer machinery — the Application
/// handler is responsible for filling these fields from the
/// real <c>IIntegrationEvent</c> instance + the message
/// envelope.
/// </summary>
public sealed record V1Envelope(
    string EventTypeName,
    DateTimeOffset OccurredAt,
    Option<FabIdentifier> Fab,
    ActorIdentifier Actor,
    Option<string> ActorUsername,
    EventIdentifier EventIdentifier,
    string Payload);

/// <summary>
/// Resource-pivot metadata for a single <c>*V1</c> type, looked
/// up at handler time from
/// <c>AuditObservability.Application.EventHandlers.V1ResourceMap</c>.
/// </summary>
public sealed record V1Mapping(
    Option<ResourceKind> Kind,
    Option<ResourceIdentifier> ResourceIdentifier)
{
    /// <summary>Mapping for a V1 whose resource shape isn't known to the registry.</summary>
    public static V1Mapping Unmapped { get; } = new(Option<ResourceKind>.None, Option<ResourceIdentifier>.None);
}
