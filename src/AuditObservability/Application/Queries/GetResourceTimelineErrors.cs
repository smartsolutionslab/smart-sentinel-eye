using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

public abstract record GetResourceTimelineError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record UnknownResourceKind(string ResourceKind)
        : GetResourceTimelineError(
            "AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND",
            $"resourceKind '{ResourceKind}' is not a known audit resource.",
            HttpStatusCode.BadRequest);

    public sealed record InvalidCursor(string Cursor)
        : GetResourceTimelineError(
            "AUDIT_TIMELINE_INVALID_CURSOR",
            $"Cursor '{Cursor}' is not a valid pagination cursor.",
            HttpStatusCode.BadRequest);

    public sealed record PageSizeOutOfRange(int PageSize, int Min, int Max)
        : GetResourceTimelineError(
            "AUDIT_TIMELINE_PAGE_SIZE_OUT_OF_RANGE",
            $"pageSize {PageSize} must be between {Min} and {Max}.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="GetResourceTimelineError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetResourceTimelineFailures
{
    public static GetResourceTimelineError UnknownResourceKind(string resourceKind) =>
        new GetResourceTimelineError.UnknownResourceKind(resourceKind);

    public static GetResourceTimelineError InvalidCursor(string cursor) =>
        new GetResourceTimelineError.InvalidCursor(cursor);

    public static GetResourceTimelineError PageSizeOutOfRange(int pageSize, int min, int max) =>
        new GetResourceTimelineError.PageSizeOutOfRange(pageSize, min, max);
}
