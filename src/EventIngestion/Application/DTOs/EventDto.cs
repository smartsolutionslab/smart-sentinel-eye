namespace SmartSentinelEye.EventIngestion.Application.DTOs;

public sealed record EventDto(
    Guid EventIdentifier,
    string Fab,
    string Source,
    string Device,
    string Kind,
    DateTimeOffset OccurredAt,
    DateTimeOffset IngestedAt,
    string Payload);
