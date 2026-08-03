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

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record OverlayRevisionStale(Guid Overlay, int ExpectedVersion, int ActualVersion)
        : BranchDraftRevisionError(
            "OVERLAY_REVISION_STALE",
            $"Overlay {Overlay} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="BranchDraftRevisionError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class BranchDraftRevisionFailures
{
    public static BranchDraftRevisionError OverlayNotFound(Guid overlay) =>
        new BranchDraftRevisionError.OverlayNotFound(overlay);

    public static BranchDraftRevisionError NoPublishedRevisionToBranchFrom(Guid overlay) =>
        new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(overlay);

    public static BranchDraftRevisionError OverlayRevisionStale(Guid overlay, int expectedVersion, int actualVersion) =>
        new BranchDraftRevisionError.OverlayRevisionStale(overlay, expectedVersion, actualVersion);
}
