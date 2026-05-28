namespace SmartSentinelEye.EventIngestion.Application.DTOs;

public sealed record DeadLetterDto(
    Guid DeadLetterIdentifier,
    string Topic,
    string RawPayload,
    string Error,
    DateTimeOffset RejectedAt);
