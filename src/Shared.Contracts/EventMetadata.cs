namespace SmartSentinelEye.Shared.Contracts;

/// <summary>
/// Common metadata carried by every <see cref="IIntegrationEvent"/>
/// (ADR-0102). Read uniformly by the AuditObservability subscriber to
/// record one audit row per event; useful for tracing + replay beyond
/// audit.
///
/// <para>
/// Primitive types are used at the wire boundary per ADR-0040;
/// value-object types stay inside their owning context.
/// </para>
/// </summary>
/// <param name="EventIdentifier">Guid v7, stable per logical event — the audit idempotency key.</param>
/// <param name="OccurredAt">When the originating domain action happened.</param>
/// <param name="Fab">Owning fab when the event is fab-scoped; otherwise <see langword="null"/>.</param>
/// <param name="Actor">Acting principal when known; otherwise <see langword="null"/>.</param>
public sealed record EventMetadata(
    Guid EventIdentifier,
    DateTimeOffset OccurredAt,
    string? Fab,
    Guid? Actor);
