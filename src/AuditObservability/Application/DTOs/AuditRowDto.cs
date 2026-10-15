namespace SmartSentinelEye.AuditObservability.Application.DTOs;

/// <summary>
/// HTTP-shaped projection of a single audit row (spec 009 FR-008
/// / FR-010). The V1 JSON, camera URLs reduced to their origin, sits in <see cref="Payload"/>;
/// the indexed metadata duplicates it for predictable client
/// access without a second JSON parse. The payload is the audit copy: a
/// camera address in it is already reduced to its origin, so the URL an
/// operator sees here is not the one the camera was registered with
/// (spec 377).
/// </summary>
public sealed record AuditRowDto(
    Guid AuditIdentifier,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? Fab,
    string EventKind,
    string? ResourceKind,
    string? ResourceIdentifier,
    Guid ActorIdentifier,
    bool ActorIsSystem,
    string? ActorUsername,
    Guid EventIdentifier,
    string Payload,
    int PayloadSizeBytes,
    short SchemaVersion,
    string FabAttribution);
