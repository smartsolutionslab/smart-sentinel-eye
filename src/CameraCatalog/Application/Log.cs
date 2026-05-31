using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected camera registration: name {CameraName} already in use.")]
    public static partial void RejectedCameraRegistrationNameInUse(ILogger logger, CameraName cameraName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered camera {CameraId} with name {CameraName}.")]
    public static partial void RegisteredCamera(ILogger logger, CameraIdentifier cameraId, CameraName cameraName);
}
