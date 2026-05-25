using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Registers a new camera. Returns the assigned identifier on success or a
/// typed RegisterCameraError on business-rule failure.
/// </summary>
public sealed record RegisterCameraCommand(
    CameraName Name,
    RtspUrl Url,
    OperatorIdentifier RegisteredBy)
    : ICommand<Result<CameraIdentifier, RegisterCameraError>>;
