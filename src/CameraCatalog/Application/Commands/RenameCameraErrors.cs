using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="RenameCameraCommand"/>
/// (ADR-0047 + ADR-0089).
/// </summary>
/// <remarks>
/// A rename is the first camera operation that can fail two <em>different</em>
/// conflict ways, and telling them apart is the point:
/// <see cref="NameTaken"/> never becomes possible by re-reading, and
/// <see cref="VersionStale"/> always does.
/// </remarks>
public abstract record RenameCameraError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// Also what another fab's camera resolves to, deliberately (spec 029
    /// FR-006). A camera record carries its RTSP address, so a distinguishable
    /// refusal lets an operator enumerate another plant's cameras one request
    /// at a time.
    /// </summary>
    public sealed record CameraNotFound(Guid Camera)
        : RenameCameraError(
            "CAMERA_NOT_FOUND",
            $"No camera with identifier '{Camera}' exists.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// Retirement is terminal (spec 028 FR-001). Renaming hardware that is gone
    /// changes nothing but the historical record, which FR-013 keeps as it was.
    /// </summary>
    public sealed record CameraRetired(Guid Camera)
        : RenameCameraError(
            "CAMERA_RETIRED",
            $"Camera '{Camera}' is retired; it cannot be renamed.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// Another <em>active</em> camera in this fab already holds the name,
    /// compared ignoring case (#1434).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The code deliberately does not end <c>_STALE</c></b> (ADR-0119). This
    /// is not a lost update: the caller's version is fine, nobody changed this
    /// camera, and re-reading it would show exactly what they already had.
    /// </para>
    /// <para>
    /// The distinction is what a caller acts on. A stale version is resolved by
    /// re-reading and reapplying; a taken name is resolved only by choosing a
    /// different one or waiting for the holder to release it. Told the wrong
    /// one, an operator retries forever against a name that belongs to somebody
    /// else — which is why <see cref="VersionStale"/> below shares neither the
    /// code nor the status.
    /// </para>
    /// </remarks>
    public sealed record NameTaken(string Name, string Fab)
        : RenameCameraError(
            "CAMERA_NAME_TAKEN",
            $"Another camera in fab '{Fab}' is already called '{Name}'. Names are unique per fab, ignoring case.",
            HttpStatusCode.Conflict);

    /// <summary>
    /// The caller quoted a version that is no longer current — somebody else
    /// changed the camera in between. Not retried automatically (ADR-0113).
    /// </summary>
    /// <remarks>
    /// Ends <c>_STALE</c> because that suffix is what identifies a lost update
    /// across every context (ADR-0119), and carries <c>412</c> as spec 029's
    /// address correction does — RFC 9110 §15.5.13 specifies it for a failed
    /// <c>If-Match</c>.
    /// </remarks>
    public sealed record VersionStale(Guid Camera, int Expected, int Actual)
        : RenameCameraError(
            "CAMERA_VERSION_STALE",
            $"Camera '{Camera}' is at version {Actual}, not {Expected}. Re-read it before reapplying your change.",
            HttpStatusCode.PreconditionFailed);
}

/// <summary>
/// Builds a <see cref="RenameCameraError"/> as the base rather than the
/// variant. Generics are invariant, so an outcome inferred from a variant does
/// not convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RenameCameraFailures
{
    public static RenameCameraError CameraNotFound(Guid camera) =>
        new RenameCameraError.CameraNotFound(camera);

    public static RenameCameraError CameraRetired(Guid camera) =>
        new RenameCameraError.CameraRetired(camera);

    public static RenameCameraError NameTaken(string name, string fab) =>
        new RenameCameraError.NameTaken(name, fab);

    public static RenameCameraError VersionStale(Guid camera, int expected, int actual) =>
        new RenameCameraError.VersionStale(camera, expected, actual);
}
