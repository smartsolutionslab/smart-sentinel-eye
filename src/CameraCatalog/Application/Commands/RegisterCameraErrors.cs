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
    /// <summary>
    /// The name is taken <em>in that fab</em> (spec 015 FR-002). Naming the fab
    /// matters for a multi-fab operator: the same name is legitimately free in
    /// another of theirs, and an unqualified "already in use" reads as a
    /// global collision that no longer exists.
    /// </summary>
    public sealed record NameAlreadyTaken(string Fab, string Name)
        : RegisterCameraError(
            "CAMERA_NAME_TAKEN",
            $"A camera named '{Name}' already exists in fab '{Fab}'.",
            HttpStatusCode.Conflict);
}

/// <summary>
/// Builds a <see cref="RegisterCameraError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RegisterCameraFailures
{
    public static RegisterCameraError NameAlreadyTaken(string fab, string name) =>
        new RegisterCameraError.NameAlreadyTaken(fab, name);
}
