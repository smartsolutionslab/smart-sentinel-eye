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
