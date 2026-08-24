using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.CameraCatalog.Domain.Camera;

namespace SmartSentinelEye.CameraCatalog.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected camera registration: name {CameraName} already in use.")]
    public static partial void RejectedCameraRegistrationNameInUse(this ILogger logger, CameraName cameraName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered camera {CameraIdentifier} with name {CameraName}.")]
    public static partial void RegisteredCamera(this ILogger logger, CameraIdentifier cameraIdentifier, CameraName cameraName);

    // No fab in the message, matching the error it accompanies: this line is
    // written for a camera in another plant as well as for one that does not
    // exist, and naming the fab would put the distinction the API refuses to
    // make into the log instead (spec 028 FR-004).
    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected camera retirement: no camera {CameraIdentifier} in this fab.")]
    public static partial void RejectedCameraRetirementNotFound(this ILogger logger, CameraIdentifier cameraIdentifier);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retired camera {CameraIdentifier} with name {CameraName}.")]
    public static partial void RetiredCamera(this ILogger logger, CameraIdentifier cameraIdentifier, CameraName cameraName);
}
