using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

public abstract record GetOverlayError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : GetOverlayError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);
}
