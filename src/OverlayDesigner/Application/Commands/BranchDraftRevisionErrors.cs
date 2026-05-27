using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

public abstract record BranchDraftRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : BranchDraftRevisionError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record NoPublishedRevisionToBranchFrom(Guid Overlay)
        : BranchDraftRevisionError(
            "OVERLAY_NO_PUBLISHED_REVISION",
            $"Overlay {Overlay} has no Published revision to branch from.",
            HttpStatusCode.Conflict);
}
