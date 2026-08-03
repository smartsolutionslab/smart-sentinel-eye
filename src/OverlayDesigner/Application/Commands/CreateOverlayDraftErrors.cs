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

/// <summary>
/// Builds a <see cref="CreateOverlayDraftError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class CreateOverlayDraftFailures
{
    public static CreateOverlayDraftError OverlayNameTaken(string name) =>
        new CreateOverlayDraftError.OverlayNameTaken(name);
}
