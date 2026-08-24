using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="ChangeCameraAddressCommand"/>
/// (ADR-0047 + ADR-0089).
/// </summary>
public abstract record ChangeCameraAddressError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// Also what another fab's camera resolves to, deliberately (FR-006). A
    /// camera record carries its RTSP address, so a distinguishable refusal
    /// lets an operator enumerate another plant's cameras one request at a
    /// time — which is why there is no "not yours" case to reach for.
    /// </summary>
    public sealed record CameraNotFound(Guid Camera)
        : ChangeCameraAddressError(
            "CAMERA_NOT_FOUND",
            $"No camera with identifier '{Camera}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// Retirement is terminal (spec 028 FR-001), and a corrected address for
    /// hardware that is gone describes nothing. Distinguishable from
    /// <see cref="CameraNotFound"/> is safe here: the caller has already been
    /// shown this camera exists, because they could not have reached this case
    /// without it being in one of their own fabs.
    /// </summary>
    public sealed record CameraRetired(Guid Camera)
        : ChangeCameraAddressError(
            "CAMERA_RETIRED",
            $"Camera '{Camera}' is retired; its address cannot be changed.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller quoted a version that is no longer current — somebody else
    /// changed the camera in between. Not retried automatically (ADR-0113):
    /// the caller re-reads, sees what changed, and decides.
    /// </summary>
    public sealed record VersionMismatch(Guid Camera, int Expected, int Actual)
        : ChangeCameraAddressError(
            "CAMERA_VERSION_MISMATCH",
            $"Camera '{Camera}' is at version {Actual}, not {Expected}. Re-read it and try again.",
            HttpStatusCode.PreconditionFailed);
}

/// <summary>
/// Builds a <see cref="ChangeCameraAddressError"/> as the base rather than the
/// variant. Generics are invariant, so an outcome inferred from a variant does
/// not convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ChangeCameraAddressFailures
{
    public static ChangeCameraAddressError CameraNotFound(Guid camera) =>
        new ChangeCameraAddressError.CameraNotFound(camera);

    public static ChangeCameraAddressError CameraRetired(Guid camera) =>
        new ChangeCameraAddressError.CameraRetired(camera);

    public static ChangeCameraAddressError VersionMismatch(Guid camera, int expected, int actual) =>
        new ChangeCameraAddressError.VersionMismatch(camera, expected, actual);
}
