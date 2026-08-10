namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

/// <summary>
/// Read-side shape for a camera in the catalog list. Primitive types only —
/// the API contract is the boundary, not domain value objects.
/// </summary>
public sealed record CameraSummaryDto(
    Guid CameraIdentifier,
    /// <summary>
    /// The fab this camera belongs to (spec 015 FR-013). On the wire because a
    /// multi-fab operator's listing can hold two rows of the same name with
    /// nothing else to tell them apart — the gap #1303 was for rules.
    /// </summary>
    string Fab,
    string Name,
    string RtspUrl,
    DateTimeOffset RegisteredAt);
