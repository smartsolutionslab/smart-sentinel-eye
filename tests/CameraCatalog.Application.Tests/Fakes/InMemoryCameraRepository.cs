using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;

/// <summary>
/// In-memory ICameraRepository for handler tests (ADR-0052 prefer hand-
/// written fakes). Behaves like the real repository within process; no EF,
/// no Postgres, no transactions.
/// </summary>
public sealed class InMemoryCameraRepository : ICameraRepository
{
    private readonly List<Camera> _cameras = [];
    private readonly List<Camera> _pendingAdds = [];
    public int SaveCallCount { get; private set; }

    public IReadOnlyList<Camera> Cameras => _cameras;

    public Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Camera? found = _cameras.FirstOrDefault(candidate => candidate.Id.Equals(camera));
        return Task.FromResult(found is null ? Option<Camera>.None : Option<Camera>.Some(found));
    }

    // Fab in the predicate, mirroring the real repository: a camera in another
    // plant is None, not "found then refused". A double that returned it and
    // trusted the caller to compare would let a handler forgetting the check
    // pass every test here while leaking another fab's RTSP address in
    // production (spec 028 FR-004).
    public Task<Option<Camera>> GetWithinFabAsync(
        FabIdentifier fab, CameraIdentifier camera, CancellationToken cancellationToken)
    {
        Camera? found = _cameras.FirstOrDefault(
            candidate => candidate.Id.Equals(camera) && candidate.Fab.Equals(fab));

        return Task.FromResult(found is null ? Option<Camera>.None : Option<Camera>.Some(found));
    }

    // Fab is part of the match, not a filter applied afterwards. Keyed on the
    // name alone this would report another plant's camera as a collision, which
    // is the bug being fixed reproduced inside the double meant to detect it.
    //
    // CameraName.Equals compares NormalizedValue, so this is case-insensitive.
    // It was already, which is exactly what made #1434 invisible for so long:
    // the double enforced the rule while CameraRepository did not, and every
    // duplicate-name test here passed against a fake that was right about a
    // production path that was wrong. The two agree now — production matches on
    // the generated `name_normalized` column (upper(name)), and NormalizedValue
    // is ToUpperInvariant. Change one of those and this comment is the reason
    // to go and change the other.
    // Retired cameras excluded, mirroring the partial unique index and the real
    // repository's predicate (spec 028 FR-006). This is the same trap as #1434
    // in the other direction: leave it out here and the double reports a
    // collision production would not, so every name-reuse test would fail
    // against correct code.
    // `excluding` is the camera that does not count as holding the name —
    // itself, during a rename (spec 033). Without it a rename finds the camera
    // it is renaming and refuses against its own name, and the case-only rename
    // `Line-4-Inlet` -> `line-4-inlet` is refused too, because both normalise
    // the same while being a real change to what is displayed.
    public Task<bool> ExistsByNameAsync(
        FabIdentifier fab,
        CameraName name,
        Option<CameraIdentifier> excluding,
        CancellationToken cancellationToken) =>
        Task.FromResult(_cameras.Any(candidate =>
            candidate.Fab.Equals(fab)
            && candidate.Name.Equals(name)
            && candidate.Status != CameraStatus.Decommissioned
            && !excluding.Match(some => candidate.Id.Equals(some), () => false)));

    public void Add(Camera camera) => _pendingAdds.Add(camera);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        _cameras.AddRange(_pendingAdds);
        _pendingAdds.Clear();
        SaveCallCount++;
        return Task.CompletedTask;
    }
}
