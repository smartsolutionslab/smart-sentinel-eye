namespace SmartSentinelEye.EventIngestion.Application.DTOs;

/// <summary>
/// <c>Fab</c>, <c>Reason</c>, <c>Kind</c>, <c>State</c> appended since spec
/// 317 (#2325), FR-008 — additive, no existing client. <c>Fab</c> and
/// <c>Kind</c> are nullable, mirroring the aggregate's own nullability
/// (spec 018 FR-010; spec 317 FR-002).
/// </summary>
public sealed record DeadLetterDto(
    Guid DeadLetterIdentifier,
    string Topic,
    string RawPayload,
    string Error,
    DateTimeOffset RejectedAt,
    string? Fab,
    string Reason,
    string? Kind,
    string State);
