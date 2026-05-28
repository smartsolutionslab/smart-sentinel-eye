using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public abstract record GetEventError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record EventNotFound(Guid Identifier)
        : GetEventError(
            "EVENT_NOT_FOUND",
            $"Event {Identifier} not found.",
            HttpStatusCode.NotFound);
}
