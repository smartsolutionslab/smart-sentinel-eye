using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

public abstract record ArchiveRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : ArchiveRevisionError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record OverlayRevisionNotFound(Guid Overlay, int RevisionNumber)
        : ArchiveRevisionError(
            "OVERLAY_REVISION_NOT_FOUND",
            $"Overlay {Overlay} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);
}
