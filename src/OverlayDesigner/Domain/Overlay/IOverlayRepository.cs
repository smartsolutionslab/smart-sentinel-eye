using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Domain.Overlay;

/// <summary>
/// Overlay-chain repository contract (ADR-0041). Implementation lives
/// in OverlayDesigner.Infrastructure; the Domain layer has no
/// persistence dependency.
/// </summary>
public interface IOverlayRepository
{
    Task<Option<Overlay>> GetByIdentifierAsync(OverlayIdentifier overlay, CancellationToken cancellationToken);

    Task<Option<Overlay>> GetByNameAsync(OverlayName name, CancellationToken cancellationToken);

    void Add(Overlay overlay);

    Task SaveAsync(CancellationToken cancellationToken);
}
