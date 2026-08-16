using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Tiles;

/// <summary>
/// Answers which of a tile set's cameras do <b>not</b> belong to a given fab
/// (spec 017 FR-014).
///
/// <para>
/// The seam exists because only CameraCatalog knows a camera's fab, and
/// because the interesting behaviour — what happens when a camera cannot be
/// resolved at all (FR-015) — is unreachable through a real HTTP client.
/// </para>
/// </summary>
public interface ICameraFabGuard
{
    /// <summary>
    /// Returns the cameras that are not in <paramref name="fab"/>, which
    /// includes any that resolve to no camera at all (FR-015). An empty result
    /// means every tile is legitimate.
    ///
    /// <para>
    /// Returns the offending identifiers rather than a boolean because the
    /// refusal has to name the tile: a layout may hold four, and "one of them
    /// is wrong" is not something an operator can act on.
    /// </para>
    ///
    /// <para>
    /// Unknown and other-fab are deliberately the same answer. A branch that
    /// treated "I could not find it" more leniently than "it is elsewhere"
    /// would make FR-014 bypassable by naming an identifier that resolves to
    /// nothing.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<CameraIdentifier>> CamerasOutsideFabAsync(
        FabIdentifier fab,
        IReadOnlyList<CameraIdentifier> cameras,
        CancellationToken cancellationToken);
}
