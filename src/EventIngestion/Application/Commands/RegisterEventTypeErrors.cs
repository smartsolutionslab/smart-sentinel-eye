using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record RegisterEventTypeError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record EventTypeAlreadyRegistered(string Fab, string Kind)
        : RegisterEventTypeError(
            "EVENT_TYPE_ALREADY_REGISTERED",
            $"Event type '{Kind}' is already registered for fab '{Fab}'.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="RegisterEventTypeError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RegisterEventTypeFailures
{
    public static RegisterEventTypeError EventTypeAlreadyRegistered(string fab, string kind) =>
        new RegisterEventTypeError.EventTypeAlreadyRegistered(fab, kind);
}
