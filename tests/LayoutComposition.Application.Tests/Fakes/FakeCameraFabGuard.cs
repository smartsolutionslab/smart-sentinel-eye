using SmartSentinelEye.LayoutComposition.Application.Tiles;
using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// Stands in for CameraCatalog (spec 017 FR-014). Holds which cameras exist in
/// which fab; anything not registered is unknown, and unknown is refused for
/// the same reason and by the same path as other-fab (FR-015).
///
/// <para>
/// <see cref="Permissive"/> is the default for the tests that predate this
/// feature and care about grids, names and versions rather than fabs —
/// without it every one of them would have to enumerate its cameras.
/// </para>
/// </summary>
public sealed class FakeCameraFabGuard : ICameraFabGuard
{
    private readonly Dictionary<Guid, FabIdentifier> _fabsByCamera = [];
    private readonly bool _permissive;

    private FakeCameraFabGuard(bool permissive) => _permissive = permissive;

    /// <summary>Accepts every camera, whatever fab is asked about.</summary>
    public static FakeCameraFabGuard Permissive() => new(permissive: true);

    /// <summary>Accepts only cameras explicitly placed in a fab.</summary>
    public static FakeCameraFabGuard Strict() => new(permissive: false);

    public FakeCameraFabGuard With(FabIdentifier fab, params CameraIdentifier[] cameras)
    {
        foreach (CameraIdentifier camera in cameras)
        {
            _fabsByCamera[camera.Value] = fab;
        }

        return this;
    }

    public Task<IReadOnlyList<CameraIdentifier>> CamerasOutsideFabAsync(
        FabIdentifier fab,
        IReadOnlyList<CameraIdentifier> cameras,
        CancellationToken cancellationToken)
    {
        if (_permissive)
        {
            return Task.FromResult<IReadOnlyList<CameraIdentifier>>([]);
        }

        IReadOnlyList<CameraIdentifier> outside =
        [
            .. cameras.Where(camera =>
                !_fabsByCamera.TryGetValue(camera.Value, out FabIdentifier? actual) || actual != fab)
        ];

        return Task.FromResult(outside);
    }
}
