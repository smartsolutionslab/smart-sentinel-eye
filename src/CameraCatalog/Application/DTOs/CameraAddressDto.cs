namespace SmartSentinelEye.CameraCatalog.Application.DTOs;

/// <summary>The full stored address of one camera and the version to quote when correcting it (ADR-0179).</summary>
public sealed record CameraAddressDto(string RtspUrl, int Version);
