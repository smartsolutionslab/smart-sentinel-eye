namespace SmartSentinelEye.StreamDistribution.Application.DTOs;

/// <summary>
/// Read-side shape for one stream's current state. Primitive types only —
/// value-object types stay inside the Domain layer.
///
/// <para>
/// <c>Fab</c> lets a multi-fab operator see which plant a stream belongs to
/// without cross-referencing the camera catalogue. Never null on a returned
/// row: an unattributed stream is returned to nobody (spec 016 FR-009).
/// </para>
/// </summary>
public sealed record StreamHealthDto(
    Guid CameraIdentifier,
    string Fab,
    string State,
    string WhepUrl,
    string TranscodeMode,
    DateTimeOffset? LastSuccessAt,
    string? Error);
