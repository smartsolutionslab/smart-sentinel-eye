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
    DateTimeOffset RegisteredAt,
    /// <summary>
    /// Spec 028 FR-007. Present on every row, not only when retired cameras
    /// were asked for: a client that opts in needs to tell the two apart, and a
    /// field that appears only sometimes is harder to consume than one that is
    /// always there.
    /// </summary>
    string Status);
