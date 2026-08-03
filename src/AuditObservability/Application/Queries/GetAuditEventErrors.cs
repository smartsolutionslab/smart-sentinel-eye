using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

public abstract record GetAuditEventError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record AuditEventNotFound(Guid AuditIdentifier)
        : GetAuditEventError(
            "AUDIT_EVENT_NOT_FOUND",
            $"No audit event with id '{AuditIdentifier}' exists.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="GetAuditEventError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetAuditEventFailures
{
    public static GetAuditEventError AuditEventNotFound(Guid auditIdentifier) =>
        new GetAuditEventError.AuditEventNotFound(auditIdentifier);
}
