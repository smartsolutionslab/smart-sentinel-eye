using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

public abstract record EditDraftRevisionError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNotFound(Guid Layout)
        : EditDraftRevisionError(
            "LAYOUT_NOT_FOUND",
            $"Layout {Layout} does not exist.",
            HttpStatusCode.NotFound);

    public sealed record LayoutRevisionNotFound(Guid Layout, int RevisionNumber)
        : EditDraftRevisionError(
            "LAYOUT_REVISION_NOT_FOUND",
            $"Layout {Layout} has no revision {RevisionNumber}.",
            HttpStatusCode.NotFound);

    public sealed record NotADraft(string FromState)
        : EditDraftRevisionError(
            "LAYOUT_REVISION_NOT_DRAFT",
            $"Revision is in state '{FromState}'; only Draft revisions can be edited in place.",
            HttpStatusCode.Conflict);
}
