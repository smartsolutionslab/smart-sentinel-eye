using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Layout-chain repository contract (ADR-0041). Implementation lives in
/// LayoutComposition.Infrastructure; the Domain layer has no persistence
/// dependency.
/// </summary>
public interface ILayoutRepository
{
    Task<Option<Layout>> GetByIdentifierAsync(LayoutIdentifier layout, CancellationToken cancellationToken);

    Task<Option<Layout>> GetByNameAsync(LayoutName name, CancellationToken cancellationToken);

    void Add(Layout layout);

    Task SaveAsync(CancellationToken cancellationToken);
}
