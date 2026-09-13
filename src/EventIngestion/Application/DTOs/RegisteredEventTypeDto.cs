namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// Read-side DTO for a registered event type. Carries <see cref="Version"/>
/// because there is no single-resource GET (spec.md FR-007) — the list is
/// the only way in, so it is the only place a caller can read the version
/// <c>DELETE /event-types/{kind}</c> needs in <c>If-Match</c>.
/// </summary>
public sealed record RegisteredEventTypeDto(
    Guid EventTypeId,
    string Fab,
    string Kind,
    string State,
    DateTimeOffset RegisteredAt,
    Guid RegisteredBy,
    int Version);
