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

    public sealed record NoPublishedRevisionToBranchFrom(Guid Layout)
        : BranchDraftRevisionError(
            "LAYOUT_NO_PUBLISHED_REVISION",
            $"Layout {Layout} has no Published revision to branch from.",
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

    public static BranchDraftRevisionError NoPublishedRevisionToBranchFrom(Guid layout) =>
        new BranchDraftRevisionError.NoPublishedRevisionToBranchFrom(layout);

    public static BranchDraftRevisionError LayoutRevisionStale(Guid layout, int expectedVersion, int actualVersion) =>
        new BranchDraftRevisionError.LayoutRevisionStale(layout, expectedVersion, actualVersion);
}
