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
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected address change for camera {Camera}: not found in the caller's fab.")]
    public static partial void RejectedCameraAddressChangeNotFound(this ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected address change for camera {Camera}: expected version {Expected}, actual {Actual}.")]
    public static partial void RejectedCameraAddressChangeStaleVersion(this ILogger logger, CameraIdentifier camera, int expected, int actual);

    [LoggerMessage(Level = LogLevel.Information, Message = "Changed camera {Camera} address to {Url}.")]
    public static partial void ChangedCameraAddress(this ILogger logger, CameraIdentifier camera, RtspUrl url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected rename for camera {Camera}: not found in the caller's fab.")]
    public static partial void RejectedCameraRenameNotFound(this ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected rename for camera {Camera}: expected version {Expected}, actual {Actual}.")]
    public static partial void RejectedCameraRenameStaleVersion(this ILogger logger, CameraIdentifier camera, int expected, int actual);

    // The name is logged because it is the operator's own input rather than
    // another camera's data — and knowing which name collided is the whole
    // content of the refusal.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected rename for camera {Camera}: {CameraName} is already taken in this fab.")]
    public static partial void RejectedCameraRenameNameTaken(this ILogger logger, CameraIdentifier camera, CameraName cameraName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed camera {Camera} to {CameraName}.")]
    public static partial void RenamedCamera(this ILogger logger, CameraIdentifier camera, CameraName cameraName);
}
