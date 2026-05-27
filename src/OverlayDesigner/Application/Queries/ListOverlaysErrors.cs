using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

public abstract record ListOverlaysError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListOverlaysError(
            "OVERLAY_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid overlay state (Draft | Published | Archived).",
            HttpStatusCode.BadRequest);
}
