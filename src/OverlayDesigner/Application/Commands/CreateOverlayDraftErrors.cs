using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="CreateOverlayDraftCommand"/> (ADR-0047 + ADR-0089).
/// </summary>
public abstract record CreateOverlayDraftError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record OverlayNameTaken(string Name)
        : CreateOverlayDraftError(
            "OVERLAY_NAME_TAKEN",
            $"A non-archived overlay with the name '{Name}' already exists.",
            HttpStatusCode.Conflict);
}
