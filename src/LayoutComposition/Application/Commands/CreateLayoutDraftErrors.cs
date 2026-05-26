using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for
/// <see cref="CreateLayoutDraftCommand"/> (ADR-0047 + ADR-0089). Each
/// case carries Code, Message, and HttpStatusCode so the API layer maps
/// to RFC 7807 Problem Details without per-case translation.
/// </summary>
public abstract record CreateLayoutDraftError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record LayoutNameTaken(string Name)
        : CreateLayoutDraftError(
            "LAYOUT_NAME_TAKEN",
            $"A non-archived layout with the name '{Name}' already exists.",
            HttpStatusCode.Conflict);
}
