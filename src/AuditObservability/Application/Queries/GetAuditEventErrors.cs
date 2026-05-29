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
