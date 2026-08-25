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

    /// <summary>
    /// Spec 037 FR-007. The code and the 409 are unchanged — the condition it
    /// names is still true, and a client may switch on it.
    ///
    /// <para>
    /// The message is not. Since ADR-0121 some chains without a Published
    /// revision <em>can</em> be branched, so telling this caller there is no
    /// Published revision describes their situation without naming their way
    /// forward. The reason they are refused is that a draft is already open,
    /// and the thing to do is edit it.
    /// </para>
    /// </summary>
    public sealed record NoPublishedRevisionToBranchFrom(Guid Overlay, int DraftRevision)
        : BranchDraftRevisionError(
            "OVERLAY_NO_PUBLISHED_REVISION",
            $"Overlay {Overlay} already has a Draft revision {DraftRevision}. Edit that draft rather than branching another.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// Spec 037 FR-009. A chain becomes recoverable exactly when every revision
    /// is Archived — which is also exactly when its name is released, because
    /// the name lookup ignores fully-archived chains. So between archiving and
    /// recovering, another overlay may legitimately have taken the name.
    ///
    /// <para>
    /// Recovering anyway would leave two live overlays sharing a name, and
    /// nothing downstream would catch it: uniqueness is checked only when an
    /// overlay is created, and <c>ix_overlays_name</c> is not unique.
    /// </para>
    ///
    /// <para>
    /// No fab here, unlike the Layout twin: overlay names are global
    /// (spec 004 FR-006), where a layout's are unique per fab (spec 017 FR-019).
    /// The twins are reflecting a difference their name rules already have.
    /// </para>
    ///
    /// <para>
    /// Reuses <c>OVERLAY_NAME_TAKEN</c> from the create path deliberately — the
    /// same condition reached by a different route, so a client already handling
    /// the create-path collision handles this one unchanged.
    /// </para>
    /// </summary>
    public sealed record OverlayNameTaken(Guid Overlay, string Name)
        : BranchDraftRevisionError(
            "OVERLAY_NAME_TAKEN",
            $"Overlay {Overlay} cannot be recovered: the name '{Name}' is now used by another overlay.",
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

    public static BranchDraftRevisionError NoPublishedRevisionToBranchFrom(Guid overlay, int draftRevision) =>
        new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(overlay, draftRevision);

    public static BranchDraftRevisionError OverlayNameTaken(Guid overlay, string name) =>
        new BranchDraftRevisionError.OverlayNameTaken(overlay, name);

    public static BranchDraftRevisionError OverlayRevisionStale(Guid overlay, int expectedVersion, int actualVersion) =>
        new BranchDraftRevisionError.OverlayRevisionStale(overlay, expectedVersion, actualVersion);
}
