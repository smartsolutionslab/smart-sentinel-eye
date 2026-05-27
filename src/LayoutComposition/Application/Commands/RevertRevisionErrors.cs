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
}
