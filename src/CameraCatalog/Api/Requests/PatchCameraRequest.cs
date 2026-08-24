namespace SmartSentinelEye.CameraCatalog.Api.Requests;

/// <summary>
/// Inbound HTTP shape for <c>PATCH /cameras/{camera}</c>. Strings on the wire,
/// parsed into value objects at the endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Was <c>ChangeCameraAddressRequest</c> until spec 033 made the name editable
/// too (ADR-0120). Renamed rather than extended in place: a record called
/// <c>ChangeCameraAddressRequest</c> carrying a name misdescribes itself, and
/// it had exactly one reference.
/// </para>
/// <para>
/// Both properties are optional and <b>exactly one</b> must be present. Not
/// because PATCH forbids more — it does not — but because each attribute has
/// its own command and its own <c>If-Match</c> check, and a request changing
/// both would need the second command to see a version the first had already
/// advanced. Supporting that means one combined command with one version
/// check, which is a larger change than either correction is worth today.
/// </para>
/// <para>
/// The fab and the identifier remain absent, and immutably so (spec 015 FR-004,
/// spec 029 FR-008) — there is nothing here that could express changing them,
/// which is a stronger guarantee than validating them away.
/// </para>
/// </remarks>
public sealed record PatchCameraRequest
{
    /// <summary>The corrected RTSP address (spec 029 FR-003). Empty means unchanged.</summary>
    public string RtspUrl { get; init; } = string.Empty;

    /// <summary>The corrected name (spec 033 FR-005). Empty means unchanged.</summary>
    public string Name { get; init; } = string.Empty;
}
