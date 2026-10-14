using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;

/// <summary>
/// Decorates <see cref="InMemoryOverlayRepository"/> and records which
/// repository method was called, in order. The refusal-order
/// characterisation tests (spec 350) need to say not just what a handler
/// answered but which lookups it made on the way and whether it saved — "the
/// name lookup is never called" is unobservable through the result alone.
/// </summary>
public sealed class RecordingOverlayRepository(InMemoryOverlayRepository inner) : IOverlayRepository
{
    private readonly List<string> calls = [];

    public IReadOnlyList<string> Calls => calls;

    public async Task<Option<Overlay>> GetByIdentifierAsync(OverlayIdentifier overlay, CancellationToken cancellationToken)
    {
        calls.Add("get-by-identifier");
        return await inner.GetByIdentifierAsync(overlay, cancellationToken);
    }

    public async Task<Option<Overlay>> GetByNameAsync(OverlayName name, CancellationToken cancellationToken)
    {
        calls.Add("get-by-name");
        return await inner.GetByNameAsync(name, cancellationToken);
    }

    public void Add(Overlay overlay)
    {
        calls.Add("add");
        inner.Add(overlay);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        calls.Add("save");
        await inner.SaveAsync(cancellationToken);
    }
}
