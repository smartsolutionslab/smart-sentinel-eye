using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="PublishRevisionCommand"/> (ADR-0047 + ADR-0089).
/// </summary>
public abstract record PublishRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNotFound(Guid Layout)
        : PublishRevisionError(
            "LAYOUT_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record LayoutRevisionNotFound(Guid Layout, int RevisionNumber)
        : PublishRevisionError(
            "LAYOUT_REVISION_NOT_FOUND",
            $"Layout {Layout} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record InvalidStateTransition(string FromState)
        : PublishRevisionError(
            "LAYOUT_REVISION_INVALID_TRANSITION",
            $"Revision is in state '{FromState}'; only Draft revisions can be published.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller acted on a chain version that has since moved on
    /// (ADR-0113 Layer 1). 409 rather than 412 so it reads as the domain
    /// conflict it is, consistent with the other Conflict cases here.
    /// </summary>
    public sealed record LayoutRevisionStale(Guid Layout, int ExpectedVersion, int ActualVersion)
        : PublishRevisionError(
            "LAYOUT_REVISION_STALE",
            $"Layout {Layout} has changed since version {ExpectedVersion} (now {ActualVersion}). Re-read it and reapply the change.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="PublishRevisionError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class PublishRevisionFailures
{
    public static PublishRevisionError LayoutNotFound(Guid layout) =>
        new PublishRevisionError.LayoutNotFound(layout);

    public static PublishRevisionError LayoutRevisionNotFound(Guid layout, int revisionNumber) =>
        new PublishRevisionError.LayoutRevisionNotFound(layout, revisionNumber);

    public static PublishRevisionError InvalidStateTransition(string fromState) =>
        new PublishRevisionError.InvalidStateTransition(fromState);

    public static PublishRevisionError LayoutRevisionStale(Guid layout, int expectedVersion, int actualVersion) =>
        new PublishRevisionError.LayoutRevisionStale(layout, expectedVersion, actualVersion);
}
