namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// Cursor-paginated page of events (spec 006 FR-018). The cursor
/// encodes <c>(ingestedAt, eventId)</c> so the next page picks up
/// strictly after the previous one.
/// </summary>
public sealed record EventPageDto(
    IReadOnlyList<EventDto> Items,
    string? NextCursor);
