using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;

/// <summary>
/// Decorates <see cref="InMemoryCameraRepository"/> and records which
/// repository method was called, in order, plus each name lookup's arguments.
/// The refusal-order characterisation tests (spec 353) need to say not just
/// what a handler answered but which lookups it made on the way, what it
/// excluded from the name check, and whether it saved: "the name lookup is
/// never called" is unobservable through the result alone.
/// </summary>
public sealed class RecordingCameraRepository(InMemoryCameraRepository inner) : ICameraRepository
{
    private readonly List<string> calls = [];
    private readonly List<(FabIdentifier Fab, CameraName Name, Option<CameraIdentifier> Excluding)> nameLookups = [];

    public IReadOnlyList<string> Calls => calls;

    public IReadOnlyList<(FabIdentifier Fab, CameraName Name, Option<CameraIdentifier> Excluding)> NameLookups => nameLookups;

    public async Task<Option<Camera>> GetByIdentifierAsync(CameraIdentifier camera, CancellationToken cancellationToken)
    {
        calls.Add("get-by-identifier");
        return await inner.GetByIdentifierAsync(camera, cancellationToken);
    }

    public async Task<Option<Camera>> GetWithinFabAsync(
        FabIdentifier fab,
        CameraIdentifier camera,
        CancellationToken cancellationToken)
    {
        calls.Add("get-within-fab");
        return await inner.GetWithinFabAsync(fab, camera, cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(
        FabIdentifier fab,
        CameraName name,
        Option<CameraIdentifier> excluding,
        CancellationToken cancellationToken)
    {
        calls.Add("exists-by-name");
        nameLookups.Add((fab, name, excluding));
        return await inner.ExistsByNameAsync(fab, name, excluding, cancellationToken);
    }

    public void Add(Camera camera)
    {
        calls.Add("add");
        inner.Add(camera);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        calls.Add("save");
        await inner.SaveAsync(cancellationToken);
    }
}
