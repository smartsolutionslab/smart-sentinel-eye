using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

public abstract record RevertRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNotFound(Guid Overlay)
        : RevertRevisionError(
            "OVERLAY_NOT_FOUND",
            $"Overlay {Overlay} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record OverlayRevisionNotFound(Guid Overlay, int RevisionNumber)
        : RevertRevisionError(
            "OVERLAY_REVISION_NOT_FOUND",
            $"Overlay {Overlay} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record NotPublished(string FromState)
        : RevertRevisionError(
            "OVERLAY_REVISION_NOT_PUBLISHED",
            $"Revision is in state '{FromState}'; only Published revisions can be reverted.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record OverlayRevisionStale(Guid Overlay, int ExpectedVersion, int ActualVersion)
        : RevertRevisionError(
            "OVERLAY_REVISION_STALE",
            $"Overlay {Overlay} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="RevertRevisionError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RevertRevisionFailures
{
    public static RevertRevisionError OverlayNotFound(Guid overlay) =>
        new RevertRevisionError.OverlayNotFound(overlay);

    public static RevertRevisionError OverlayRevisionNotFound(Guid overlay, int revisionNumber) =>
        new RevertRevisionError.OverlayRevisionNotFound(overlay, revisionNumber);

    public static RevertRevisionError NotPublished(string fromState) =>
        new RevertRevisionError.NotPublished(fromState);

    public static RevertRevisionError OverlayRevisionStale(Guid overlay, int expectedVersion, int actualVersion) =>
        new RevertRevisionError.OverlayRevisionStale(overlay, expectedVersion, actualVersion);
}
