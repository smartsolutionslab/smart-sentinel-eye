namespace SmartSentinelEye.StreamDistribution.Application.DTOs;

/// <summary>
/// Read-side shape for one stream's current state. Primitive types only —
/// value-object types stay inside the Domain layer.
/// </summary>
public sealed record StreamHealthDto(
    Guid CameraIdentifier,
    string State,
    string WhepUrl,
    string TranscodeMode,
    DateTimeOffset? LastSuccessAt,
    string? Error);
