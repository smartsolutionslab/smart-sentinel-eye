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

/// <summary>
/// Builds a <see cref="ReportStreamHealthError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ReportStreamHealthFailures
{
    public static ReportStreamHealthError StreamNotFound(Guid camera) =>
        new ReportStreamHealthError.StreamNotFound(camera);

    public static ReportStreamHealthError InvalidStateTransition(string from, string to, string reason) =>
        new ReportStreamHealthError.InvalidStateTransition(from, to, reason);
}
