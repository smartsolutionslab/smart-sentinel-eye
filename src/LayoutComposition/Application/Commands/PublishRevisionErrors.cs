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
}
