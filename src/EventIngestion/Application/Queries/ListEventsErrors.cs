using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public abstract record ListEventsError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidCursor(string Cursor)
        : ListEventsError(
            "EVENT_LIST_INVALID_CURSOR",
            $"Cursor '{Cursor}' is not a valid pagination cursor.",
            HttpStatusCode.BadRequest);

    public sealed record PageSizeOutOfRange(int PageSize, int Min, int Max)
        : ListEventsError(
            "EVENT_LIST_PAGE_SIZE_OUT_OF_RANGE",
            $"pageSize {PageSize} must be between {Min} and {Max}.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ListEventsError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListEventsFailures
{
    public static ListEventsError InvalidCursor(string cursor) =>
        new ListEventsError.InvalidCursor(cursor);

    public static ListEventsError PageSizeOutOfRange(int pageSize, int min, int max) =>
        new ListEventsError.PageSizeOutOfRange(pageSize, min, max);
}
