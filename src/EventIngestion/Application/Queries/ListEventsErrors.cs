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
