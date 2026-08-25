using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

public abstract record BranchDraftRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNotFound(Guid Layout)
        : BranchDraftRevisionError(
            "LAYOUT_NOT_FOUND",
            $"Layout {Layout} does not exist.",
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
    public sealed record NoPublishedRevisionToBranchFrom(Guid Layout, int DraftRevision)
        : BranchDraftRevisionError(
            "LAYOUT_NO_PUBLISHED_REVISION",
            $"Layout {Layout} already has a Draft revision {DraftRevision}. Edit that draft rather than branching another.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// Spec 037 FR-009. A chain becomes recoverable exactly when every revision
    /// is Archived — which is also exactly when its name is released, because
    /// both name lookups ignore fully-archived chains. So between archiving and
    /// recovering, another layout may legitimately have taken the name.
    ///
    /// <para>
    /// Recovering anyway would leave two live layouts sharing a name in one fab,
    /// and nothing downstream would catch it: uniqueness is checked only when a
    /// layout is created, and <c>ix_layouts_fab_name</c> is not unique.
    /// </para>
    ///
    /// <para>
    /// Reuses <c>LAYOUT_NAME_TAKEN</c> from the create path deliberately — the
    /// same condition reached by a different route, so a client already handling
    /// the create-path collision handles this one unchanged.
    /// </para>
    /// </summary>
    public sealed record LayoutNameTaken(Guid Layout, string Name, string Fab)
        : BranchDraftRevisionError(
            "LAYOUT_NAME_TAKEN",
            $"Layout {Layout} cannot be recovered: the name '{Name}' is now used by another layout in fab {Fab}.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record LayoutRevisionStale(Guid Layout, int ExpectedVersion, int ActualVersion)
        : BranchDraftRevisionError(
            "LAYOUT_REVISION_STALE",
            $"Layout {Layout} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
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
    public static BranchDraftRevisionError LayoutNotFound(Guid layout) =>
        new BranchDraftRevisionError.LayoutNotFound(layout);

    public static BranchDraftRevisionError NoPublishedRevisionToBranchFrom(Guid layout, int draftRevision) =>
        new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(layout, draftRevision);

    public static BranchDraftRevisionError LayoutNameTaken(Guid layout, string name, string fab) =>
        new BranchDraftRevisionError.LayoutNameTaken(layout, name, fab);

    public static BranchDraftRevisionError LayoutRevisionStale(Guid layout, int expectedVersion, int actualVersion) =>
        new BranchDraftRevisionError.LayoutRevisionStale(layout, expectedVersion, actualVersion);
}
