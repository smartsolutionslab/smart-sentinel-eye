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
