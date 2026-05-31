using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Stream already exists for camera {Camera}; skipping provision (idempotent).")]
    public static partial void StreamAlreadyExists(ILogger logger, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MediaMTX path registration failed for camera {Camera}.")]
    public static partial void PathRegistrationFailed(ILogger logger, Exception exception, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned stream {Stream} for camera {Camera} at path {Path}.")]
    public static partial void ProvisionedStream(ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected health transition for camera {Camera}.")]
    public static partial void RejectedHealthTransition(ILogger logger, Exception exception, CameraIdentifier camera);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provision attempt failed for camera {Camera}: {Code} {Message}.")]
    public static partial void ProvisionAttemptFailed(ILogger logger, CameraIdentifier camera, string code, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream {Stream} provisioned for camera {Camera} at path {Path} by {Operator}.")]
    public static partial void StreamProvisioned(ILogger logger, StreamIdentifier stream, CameraIdentifier camera, MediaMtxPath path, OperatorIdentifier @operator);
}
