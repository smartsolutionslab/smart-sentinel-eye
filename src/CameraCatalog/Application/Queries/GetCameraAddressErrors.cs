using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Queries;

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
