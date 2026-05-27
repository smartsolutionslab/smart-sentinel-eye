using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;

public sealed class InMemoryOverlayRepository : IOverlayRepository
{
    private readonly List<Overlay> _overlays = new();

    public IReadOnlyList<Overlay> Overlays => _overlays;

    public Task<Option<Overlay>> GetByIdentifierAsync(OverlayIdentifier overlay, CancellationToken cancellationToken)
    {
        Overlay? found = _overlays.SingleOrDefault(candidate => candidate.Id == overlay);
        return Task.FromResult(found is null ? Option<Overlay>.None : Option<Overlay>.Some(found));
    }

    public Task<Option<Overlay>> GetByNameAsync(OverlayName name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        Overlay? found = _overlays.SingleOrDefault(candidate =>
            candidate.Name == name &&
            candidate.Revisions.Any(r => r.State != OverlayRevisionState.Archived));
        return Task.FromResult(found is null ? Option<Overlay>.None : Option<Overlay>.Some(found));
    }

    public void Add(Overlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlays.Add(overlay);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        foreach (Overlay overlay in _overlays)
        {
            overlay.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
