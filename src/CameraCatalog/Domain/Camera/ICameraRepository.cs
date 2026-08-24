using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Domain.Camera;

/// <summary>
/// Camera repository contract (ADR-0041). Implementation lives in
/// CameraCatalog.Infrastructure; the Domain layer has no persistence
/// dependency.
/// </summary>
public interface ICameraRepository
{
    Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken);

    /// <summary>
    /// The camera, only if it belongs to this fab (spec 028 FR-004). Another
    /// fab's camera comes back as <see cref="Option{T}.None"/> — the same
    /// answer as an identifier that names nothing.
    ///
    /// <para>
    /// A separate read rather than a fab check after
    /// <see cref="GetByIdentifierAsync(CameraIdentifier, CancellationToken)"/>,
    /// so the guarantee is structural: a caller cannot forget the check, and
    /// another plant's camera is never loaded in the first place. A camera's
    /// record carries its RTSP address, so "never loaded" is worth more than
    /// "loaded and then refused" (#1397).
    /// </para>
    /// </summary>
    Task<Option<Camera>> GetWithinFabAsync(
        FabIdentifier fab, CameraIdentifier camera, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the fab already holds a live camera of this name. Takes a fab
    /// because a name is unique only within one (spec 015) — without it the
    /// question is ambiguous the moment two plants use the same name, which is
    /// precisely what this feature allows.
    ///
    /// <para>
    /// <paramref name="excluding"/> names a camera that does not count as
    /// holding the name — itself. Registration passes
    /// <see cref="Option{T}.None"/>, because the camera does not exist yet. A
    /// <b>rename</b> must pass the camera being renamed, or it finds itself:
    /// that camera is active, in that fab, and holds that very normalised name
    /// whenever the rename is a no-op or changes only letter case. It would be
    /// refused against its own name (spec 033 FR-010).
    /// </para>
    ///
    /// <para>
    /// One method with an exclusion rather than two methods, deliberately. This
    /// predicate carries the whole uniqueness rule — per fab, case-insensitive,
    /// retired cameras excluded — and it has already been enforced
    /// inconsistently once: spec 028 found it missing the
    /// <c>status &lt;&gt; 'Decommissioned'</c> filter that
    /// <c>ux_cameras_fab_name_normalized_active</c> has always had. A second
    /// method would be a second place for that to drift.
    /// </para>
    /// </summary>
    Task<bool> ExistsByNameAsync(
        FabIdentifier fab,
        CameraName name,
        Option<CameraIdentifier> excluding,
        CancellationToken cancellationToken);

    void Add(Camera camera);

    Task SaveAsync(CancellationToken cancellationToken);
}
