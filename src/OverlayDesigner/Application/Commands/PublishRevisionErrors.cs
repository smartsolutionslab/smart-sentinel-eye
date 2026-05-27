using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="PublishRevisionCommand"/> (ADR-0047 + ADR-0089).
/// </summary>
public abstract record PublishRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : PublishRevisionError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record OverlayRevisionNotFound(Guid Overlay, int RevisionNumber)
        : PublishRevisionError(
            "OVERLAY_REVISION_NOT_FOUND",
            $"Overlay {Overlay} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record InvalidStateTransition(string FromState)
        : PublishRevisionError(
            "OVERLAY_REVISION_INVALID_TRANSITION",
            $"Revision is in state '{FromState}'; only Draft revisions can be published.",
            HttpStatusCode.Conflict);
}
