using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

public abstract record GetOverlaySnapshotError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// The overlay isn't in the reverse-index. Two reasons:
    /// <list type="bullet">
    /// <item>It was never Published (only Published overlays land in
    ///   the index via <c>OverlayRevisionPublishedV1</c>).</item>
    /// <item>Its Published revision was Archived, dropping it from
    ///   the index per FR-014.</item>
    /// </list>
    /// Either way the kiosk should bounce back to the picker.
    /// </summary>
    public sealed record OverlayNotInReverseIndex(Guid Overlay)
        : GetOverlaySnapshotError(
            "OVERLAY_NOT_IN_REVERSE_INDEX",
            $"Overlay {Overlay} is not currently published.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="GetOverlaySnapshotError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetOverlaySnapshotFailures
{
    public static GetOverlaySnapshotError OverlayNotInReverseIndex(Guid overlay) =>
        new GetOverlaySnapshotError.OverlayNotInReverseIndex(overlay);
}
