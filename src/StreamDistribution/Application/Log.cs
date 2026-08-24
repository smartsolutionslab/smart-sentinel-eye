using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Stream already exists for camera {Camera}; skipping provision (idempotent).")]
    public static partial void StreamAlreadyExists(this ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MediaMTX path registration failed for camera {Camera}.")]
    public static partial void PathRegistrationFailed(this ILogger logger, Exception exception, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned stream {Stream} for camera {Camera} at path {Path}.")]
    public static partial void ProvisionedStream(this ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected health transition for camera {Camera}.")]
    public static partial void RejectedHealthTransition(this ILogger logger, Exception exception, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provision attempt failed for camera {Camera}: {Code} {Message}.")]
    public static partial void ProvisionAttemptFailed(this ILogger logger, CameraIdentifier camera, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retire attempt failed for camera {Camera}: {Code} {Message}.")]
    public static partial void RetireAttemptFailed(this ILogger logger, CameraIdentifier camera, string code, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Camera {Camera} was registered without a fab; no stream provisioned (spec 016 FR-004).")]
    public static partial void CameraRegisteredWithoutFab(this ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera {Camera} was retired but has no provisioned stream; nothing to retire.")]
    public static partial void NoStreamToRetire(this ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MediaMTX path removal failed for camera {Camera}; the stream is already retired.")]
    public static partial void PathRemovalFailed(this ILogger logger, Exception exception, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retired stream {Stream} for camera {Camera} and removed path {Path}.")]
    public static partial void RetiredStream(this ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream {Stream} provisioned for camera {Camera} at path {Path} by {Operator}.")]
    public static partial void StreamProvisioned(this ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path, OperatorIdentifier @operator);
}
