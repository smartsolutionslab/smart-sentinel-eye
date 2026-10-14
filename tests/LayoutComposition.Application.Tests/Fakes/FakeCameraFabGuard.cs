using SmartSentinelEye.LayoutComposition.Application.Tiles;

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
///
/// <para>
/// <see cref="Calls"/> counts how often CameraCatalog was asked, and
/// <see cref="Unreachable"/> stands in for it being down (spec 359).
/// </para>
/// </summary>
public sealed class FakeCameraFabGuard : ICameraFabGuard
{
    private readonly Dictionary<Guid, FabIdentifier> fabsByCamera = [];
    private readonly bool permissive;
    private readonly bool unreachable;

    private FakeCameraFabGuard(bool permissive, bool unreachable = false)
    {
        this.permissive = permissive;
        this.unreachable = unreachable;
    }

    /// <summary>How many times <see cref="CamerasOutsideFabAsync"/> was called.</summary>
    public int Calls { get; private set; }

    /// <summary>Accepts every camera, whatever fab is asked about.</summary>
    public static FakeCameraFabGuard Permissive() => new(permissive: true);

    /// <summary>Accepts only cameras explicitly placed in a fab.</summary>
    public static FakeCameraFabGuard Strict() => new(permissive: false);

    /// <summary>Fails every ask the way a CameraCatalog outage does.</summary>
    public static FakeCameraFabGuard Unreachable() => new(permissive: false, unreachable: true);

    public FakeCameraFabGuard With(FabIdentifier fab, params CameraIdentifier[] cameras)
    {
        foreach (var camera in cameras)
        {
            fabsByCamera[camera.Value] = fab;
        }

        return this;
    }

    public Task<IReadOnlyList<CameraIdentifier>> CamerasOutsideFabAsync(
        FabIdentifier fab,
        IReadOnlyList<CameraIdentifier> cameras,
        CancellationToken cancellationToken)
    {
        Calls++;

        if (unreachable)
        {
            throw new HttpRequestException("CameraCatalog is unreachable.");
        }

        if (permissive)
        {
            return Task.FromResult<IReadOnlyList<CameraIdentifier>>([]);
        }

        IReadOnlyList<CameraIdentifier> outside =
        [
            .. cameras.Where(camera =>
                !fabsByCamera.TryGetValue(camera.Value, out var actual) || actual != fab)
        ];

        return Task.FromResult(outside);
    }
}
