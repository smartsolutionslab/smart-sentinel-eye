using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

public abstract record SearchAuditError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidCursor(string Cursor)
        : SearchAuditError(
            "AUDIT_SEARCH_INVALID_CURSOR",
            $"Cursor '{Cursor}' is not a valid pagination cursor.",
            HttpStatusCode.BadRequest);

    public sealed record PageSizeOutOfRange(int PageSize, int Min, int Max)
        : SearchAuditError(
            "AUDIT_SEARCH_PAGE_SIZE_OUT_OF_RANGE",
            $"pageSize {PageSize} must be between {Min} and {Max}.",
            HttpStatusCode.BadRequest);

    public sealed record InvalidResourceKind(string ResourceKind)
        : SearchAuditError(
            "AUDIT_SEARCH_INVALID_RESOURCE_KIND",
            $"resourceKind '{ResourceKind}' is not a known audit resource.",
            HttpStatusCode.BadRequest);
}
