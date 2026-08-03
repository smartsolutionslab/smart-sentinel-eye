using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

public abstract record RevertRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNotFound(Guid Layout)
        : RevertRevisionError(
            "LAYOUT_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record LayoutRevisionNotFound(Guid Layout, int RevisionNumber)
        : RevertRevisionError(
            "LAYOUT_REVISION_NOT_FOUND",
            $"Layout {Layout} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record NotPublished(string FromState)
        : RevertRevisionError(
            "LAYOUT_REVISION_NOT_PUBLISHED",
            $"Revision is in state '{FromState}'; only Published revisions can be reverted.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record LayoutRevisionStale(Guid Layout, int ExpectedVersion, int ActualVersion)
        : RevertRevisionError(
            "LAYOUT_REVISION_STALE",
            $"Layout {Layout} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
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
    public static RevertRevisionError LayoutNotFound(Guid layout) =>
        new RevertRevisionError.LayoutNotFound(layout);

    public static RevertRevisionError LayoutRevisionNotFound(Guid layout, int revisionNumber) =>
        new RevertRevisionError.LayoutRevisionNotFound(layout, revisionNumber);

    public static RevertRevisionError NotPublished(string fromState) =>
        new RevertRevisionError.NotPublished(fromState);

    public static RevertRevisionError LayoutRevisionStale(Guid layout, int expectedVersion, int actualVersion) =>
        new RevertRevisionError.LayoutRevisionStale(layout, expectedVersion, actualVersion);
}
