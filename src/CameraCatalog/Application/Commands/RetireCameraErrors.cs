using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for RetireCameraCommand (ADR-0047 +
/// ADR-0089). Each case carries Code, Message, and HttpStatusCode so the API
/// layer maps to RFC 7807 Problem Details without per-case translation.
/// </summary>
public abstract record RetireCameraError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// <b>The only failure this command has, and deliberately so.</b> A camera
    /// belonging to another fab resolves here too, rather than to a distinct
    /// "not yours" case (FR-004).
    ///
    /// <para>
    /// That is a security property, not a courtesy. A distinguishable refusal
    /// would let an operator enumerate another plant's cameras one request at
    /// a time, and a camera's record carries its RTSP address — so confirming
    /// one exists is a step toward reaching its video (#1397).
    /// </para>
    ///
    /// <para>
    /// The message names no fab for the same reason: it must read identically
    /// whether the identifier belongs to another plant or to nothing at all.
    /// </para>
    /// </summary>
    public sealed record CameraNotFound(Guid Camera)
        : RetireCameraError(
            "CAMERA_NOT_FOUND",
            $"No camera '{Camera}' exists.",
            HttpStatusCode.NotFound);
}

/// <summary>
/// Builds a <see cref="RetireCameraError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RetireCameraFailures
{
    public static RetireCameraError CameraNotFound(Guid camera) =>
        new RetireCameraError.CameraNotFound(camera);
}
