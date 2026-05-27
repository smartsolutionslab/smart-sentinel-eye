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
}
