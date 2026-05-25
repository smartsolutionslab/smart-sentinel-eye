using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Api.Requests;

/// <summary>
/// Inbound HTTP shape for POST /cameras. Strings on the wire; value objects
/// after Deconstruct. Per ADR-0069, request DTOs expose a custom Deconstruct
/// so endpoints destructure into typed VOs at the boundary.
/// </summary>
public sealed record RegisterCameraRequest
{
    public required string Name { get; init; }

    public required string RtspUrl { get; init; }

    public void Deconstruct(out CameraName name, out RtspUrl url)
    {
        name = CameraName.From(Name);
        url = Domain.Camera.RtspUrl.From(RtspUrl);
    }
}
