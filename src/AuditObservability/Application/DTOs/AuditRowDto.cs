namespace SmartSentinelEye.AuditObservability.Application.DTOs;

/// <summary>
/// HTTP-shaped projection of a single audit row (spec 009 FR-008
/// / FR-010). The verbatim V1 JSON sits in <see cref="Payload"/>;
/// the indexed metadata duplicates it for predictable client
/// access without a second JSON parse.
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
    short SchemaVersion);
