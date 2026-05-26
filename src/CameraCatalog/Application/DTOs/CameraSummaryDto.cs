namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

/// <summary>
/// Read-side shape for a camera in the catalog list. Primitive types only —
/// the API contract is the boundary, not domain value objects.
/// </summary>
public sealed record CameraSummaryDto(
    Guid CameraIdentifier,
    string Name,
    string RtspUrl,
    DateTimeOffset RegisteredAt);
