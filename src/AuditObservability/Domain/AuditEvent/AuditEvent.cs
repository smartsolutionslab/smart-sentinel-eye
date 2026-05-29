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
    public const short CurrentSchemaVersion = 1;

    public AuditEventIdentifier Id { get; private init; }

    public DateTimeOffset OccurredAt { get; private init; }

    public DateTimeOffset ReceivedAt { get; private init; }

    public FabIdentifier? Fab { get; private init; }

    public EventKind EventKind { get; private init; } = null!;

    public ResourceKind? ResourceKind { get; private init; }

    public ResourceIdentifier? ResourceIdentifier { get; private init; }

    public ActorIdentifier Actor { get; private init; } = ActorIdentifier.System;

    public string? ActorUsername { get; private init; }

    public EventIdentifier EventIdentifier { get; private init; } = null!;

    public string Payload { get; private init; } = string.Empty;

    public int PayloadSizeBytes { get; private init; }

    public short SchemaVersion { get; private init; }

    private AuditEvent() { }

    /// <summary>
    /// Builds a row from the inbound integration event + its
    /// envelope-style metadata. The handler in the Application
    /// layer plugs in the <see cref="V1Mapping"/> derived from
    /// the V1's runtime type; <see cref="IClock"/> stamps
    /// <see cref="ReceivedAt"/> at handler-local time so a
    /// queue backlog is visible as the gap to
    /// <see cref="OccurredAt"/>.
    /// </summary>
    public static AuditEvent From(V1Envelope envelope, V1Mapping mapping, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(clock);

        return new AuditEvent
        {
            Id = AuditEventIdentifier.New(),
            OccurredAt = envelope.OccurredAt,
            ReceivedAt = clock.UtcNow,
            Fab = envelope.Fab.HasValue ? envelope.Fab.Value : null,
            EventKind = EventKind.From(envelope.EventTypeName),
            ResourceKind = mapping.Kind.HasValue ? mapping.Kind.Value : null,
            ResourceIdentifier = mapping.ResourceIdentifier.HasValue ? mapping.ResourceIdentifier.Value : null,
            Actor = envelope.Actor,
            ActorUsername = envelope.ActorUsername.HasValue ? envelope.ActorUsername.Value : null,
            EventIdentifier = envelope.EventIdentifier,
            Payload = envelope.Payload,
            PayloadSizeBytes = System.Text.Encoding.UTF8.GetByteCount(envelope.Payload),
            SchemaVersion = CurrentSchemaVersion,
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
    public static V1Mapping Unmapped { get; } =
        new(Option<ResourceKind>.None, Option<ResourceIdentifier>.None);
}
