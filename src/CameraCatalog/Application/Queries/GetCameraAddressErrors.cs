using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

/// <summary>
/// Failure hierarchy for <see cref="GetCameraAddressQuery"/> (ADR-0047 + ADR-0089).
///
/// <para>
/// <b>One case, and that is the requirement.</b> A camera in a fab the caller does not hold resolves to the
/// same <see cref="CameraNotFound"/> as an identifier that was never registered - not a sibling case with a
/// different message (FR-A04). The same holds for a retired camera (FR-A04a). This route returns the full
/// stored address, so a distinguishable refusal lets an operator enumerate another plant's cameras one
/// request at a time, or learn which identifiers once existed. Adding a "not yours" or "retired" variant
/// here is exactly how that regresses, which is why there is nowhere to put one.
/// </para>
/// </summary>
public abstract record GetCameraAddressError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record CameraNotFound(Guid Camera)
        : GetCameraAddressError(
            "CAMERA_NOT_FOUND",
            $"No camera with identifier '{Camera}' exists.",
            HttpStatusCode.NotFound);
}

public static class GetCameraAddressFailures
{
    public static GetCameraAddressError CameraNotFound(Guid camera) =>
        new GetCameraAddressError.CameraNotFound(camera);
}
