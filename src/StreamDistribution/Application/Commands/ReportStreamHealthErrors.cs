using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="ReportStreamHealthCommand"/>.
/// </summary>
public abstract record ReportStreamHealthError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record StreamNotFound(Guid Camera)
        : ReportStreamHealthError(
            "STREAM_NOT_FOUND",
            $"No stream is provisioned for camera {Camera}.",
            HttpStatusCode.NotFound);

    public sealed record InvalidStateTransition(string From, string To, string Reason)
        : ReportStreamHealthError(
            "STREAM_INVALID_STATE_TRANSITION",
            $"Cannot transition stream from {From} to {To}: {Reason}",
            HttpStatusCode.Conflict);
}
