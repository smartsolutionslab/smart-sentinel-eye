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

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record OverlayRevisionStale(Guid Overlay, int ExpectedVersion, int ActualVersion)
        : PublishRevisionError(
            "OVERLAY_REVISION_STALE",
            $"Overlay {Overlay} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}
