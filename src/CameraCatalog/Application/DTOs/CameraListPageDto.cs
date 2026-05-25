namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

/// <summary>
/// Paginated camera list response per spec 001-register-camera FR-007 +
/// FR-007b. Carries the page slice, the total matching count, and the
/// echoed offset/limit so clients can render pagination controls without
/// extra round-trips.
/// </summary>
public sealed record CameraListPageDto(
    IReadOnlyList<CameraSummaryDto> Items,
    int Count,
    int Offset,
    int Limit);
