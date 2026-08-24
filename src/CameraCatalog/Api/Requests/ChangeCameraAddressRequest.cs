namespace SmartSentinelEye.CameraCatalog.Api.Requests;

/// <summary>
/// Inbound HTTP shape for PATCH /cameras/{camera}. A string on the wire,
/// parsed into a value object at the endpoint. No custom Deconstruct, unlike
/// RegisterCameraRequest: one field cannot be deconstructed in C#, and member
/// access reads better than a discard list would anyway (CLAUDE.md).
/// </summary>
/// <remarks>
/// Carries the address alone. The name is not editable (spec 029 FR-012,
/// tracked as #1850) and the fab and identifier are immutable (FR-008, FR-009),
/// so there is nothing else a correction could express — which is a stronger
/// guarantee than validating them away would be.
/// </remarks>
public sealed record ChangeCameraAddressRequest
{
    public required string RtspUrl { get; init; }
}
