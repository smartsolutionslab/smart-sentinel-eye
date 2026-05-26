using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

public abstract record ListLayoutsError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListLayoutsError(
            "LAYOUT_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid layout state (Draft | Published | Archived).",
            HttpStatusCode.BadRequest);
}
