using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for RegisterCameraCommand (ADR-0047 +
/// ADR-0089). Each case carries Code, Message, and HttpStatusCode so the API
/// layer maps to RFC 7807 Problem Details without per-case translation.
/// </summary>
public abstract record RegisterCameraError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record NameAlreadyTaken()
        : RegisterCameraError("CAMERA_NAME_TAKEN", "Camera name already in use.", HttpStatusCode.Conflict);
}
