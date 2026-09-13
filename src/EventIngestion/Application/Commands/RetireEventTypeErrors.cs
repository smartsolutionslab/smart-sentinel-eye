using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Commands;

public abstract record RetireEventTypeError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// Also the answer for an already-retired type and for a type in a fab the
    /// caller does not hold (spec.md FR-005) — the repository lookup is over
    /// registered entries in the caller's fabs, so all three are genuinely
    /// "not found" from where the caller stands.
    /// </summary>
    public sealed record EventTypeNotFound(string Kind)
        : RetireEventTypeError(
            "EVENT_TYPE_NOT_FOUND",
            $"No registered event type '{Kind}' exists in a fab you hold.",
            HttpStatusCode.NotFound);

    public sealed record EventTypeStaleVersion(string Kind, int ExpectedVersion, int ActualVersion)
        : RetireEventTypeError(
            "EVENT_TYPE_STALE_VERSION",
            $"Event type '{Kind}' has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="RetireEventTypeError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RetireEventTypeFailures
{
    public static RetireEventTypeError EventTypeNotFound(string kind) =>
        new RetireEventTypeError.EventTypeNotFound(kind);

    public static RetireEventTypeError EventTypeStaleVersion(string kind, int expectedVersion, int actualVersion) =>
        new RetireEventTypeError.EventTypeStaleVersion(kind, expectedVersion, actualVersion);
}
