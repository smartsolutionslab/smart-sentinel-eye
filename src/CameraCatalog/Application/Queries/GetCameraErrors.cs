using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="GetCameraQuery"/> (ADR-0047
/// + ADR-0089).
///
/// <para>
/// <b>One case, and that is the requirement.</b> A camera in a fab the caller
/// does not hold resolves to the same <see cref="CameraNotFound"/> as an
/// identifier that was never registered — not a sibling case with a different
/// message (FR-006). A camera record carries its RTSP address, so a
/// distinguishable refusal lets an operator enumerate another plant's cameras
/// one request at a time. Adding a "not yours" variant here is exactly how
/// that regresses, which is why there is nowhere to put one.
/// </para>
/// </summary>
public abstract record GetCameraError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record CameraNotFound(Guid Camera)
        : GetCameraError(
            "CAMERA_NOT_FOUND",
            $"No camera with identifier '{Camera}' exists.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="GetCameraError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetCameraFailures
{
    public static GetCameraError CameraNotFound(Guid camera) =>
        new GetCameraError.CameraNotFound(camera);
}
