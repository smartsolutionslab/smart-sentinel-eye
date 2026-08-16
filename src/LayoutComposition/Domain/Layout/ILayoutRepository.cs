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

    /// <summary>
    /// Looks a layout up by name **within one fab** (spec 017 FR-019).
    ///
    /// <para>
    /// The fab is not optional and the lookup is not global. A name held in
    /// another fab must be invisible here: the caller of this method turns a
    /// hit into <c>409 LAYOUT_NAME_TAKEN</c>, and answering that for a layout
    /// the operator cannot see would confirm its existence — the same
    /// enumeration leak FR-006 closes on the read path.
    /// </para>
    /// </summary>
    Task<Option<Layout>> GetByNameAsync(FabIdentifier fab, LayoutName name, CancellationToken cancellationToken);

    void Add(Layout layout);

    Task SaveAsync(CancellationToken cancellationToken);
}
