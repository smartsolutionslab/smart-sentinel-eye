namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

/// <summary>
/// Read-side shape for a single camera (spec 029 FR-001). Primitive types
/// only — the API contract is the boundary, not domain value objects.
/// </summary>
public sealed record CameraDto(
    Guid CameraIdentifier,
    /// <summary>
    /// Optimistic-concurrency version (ADR-0113). Echoed back via
    /// <c>If-Match</c> to change the camera; also on the body rather than the
    /// <c>ETag</c> alone, so the listing can hand every row a version without
    /// a per-row fetch — the reason <c>RuleDto</c> gives for the same choice.
    ///
    /// <para>
    /// Nothing exposed a camera's version before this feature, which is why
    /// the read had to land before the edit could: a caller cannot quote what
    /// it cannot read.
    /// </para>
    /// </summary>
    int Version,
    string Fab,
    string Name,
    string RtspUrl,
    DateTimeOffset RegisteredAt,
    /// <summary>
    /// <c>Registered</c> or <c>Decommissioned</c>. A retired camera is
    /// returned here rather than reported missing (FR-002): retirement takes a
    /// camera out of the default *listing*, but "tell me about this camera" is
    /// asked because the caller already holds its identifier, and answering
    /// "not found" for a record that exists would be a lie the audit trail
    /// contradicts.
    /// </summary>
    string Status);
