namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// Read-side DTO for a declared source mode. Carries <see cref="Version"/>
/// because <c>PUT /event-sources/{source}/mode</c> needs it in
/// <c>If-Match</c>, and there is no single-resource GET — the list is the
/// only way in (mirrors <c>RegisteredEventTypeDto</c>).
/// </summary>
public sealed record SourceModeDto(
    Guid SourceModeId,
    string Fab,
    string Source,
    string Mode,
    DateTimeOffset DeclaredAt,
    Guid DeclaredBy,
    int Version);
