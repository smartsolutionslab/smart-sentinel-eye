using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Queries;

public abstract record GetStreamError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record StreamNotFound(Guid Camera)
        : GetStreamError(
            "STREAM_NOT_FOUND",
            $"No stream is provisioned for camera {Camera}.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="GetStreamError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetStreamFailures
{
    public static GetStreamError StreamNotFound(Guid camera) =>
        new GetStreamError.StreamNotFound(camera);
}
