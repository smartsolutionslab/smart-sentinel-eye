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

/// <summary>
/// Builds a <see cref="GetEventError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetEventFailures
{
    public static GetEventError EventNotFound(Guid identifier) =>
        new GetEventError.EventNotFound(identifier);
}
