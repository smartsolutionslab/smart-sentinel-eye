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
        Camera found = _cameras.FirstOrDefault(candidate => candidate.Id.Equals(camera));
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
    public Task<bool> ExistsByNameAsync(
        FabIdentifier fab, CameraName name, CancellationToken cancellationToken) =>
        Task.FromResult(_cameras.Any(candidate =>
            candidate.Fab.Equals(fab) && candidate.Name.Equals(name)));

    public void Add(Camera camera) => _pendingAdds.Add(camera);

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        _cameras.AddRange(_pendingAdds);
        _pendingAdds.Clear();
        SaveCallCount++;
        return Task.CompletedTask;
    }
}
