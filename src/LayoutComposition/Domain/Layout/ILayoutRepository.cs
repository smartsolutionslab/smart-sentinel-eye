using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Layout;

/// <summary>
/// Layout-chain repository contract (ADR-0041). Implementation lives in
/// LayoutComposition.Infrastructure; the Domain layer has no persistence
/// dependency.
/// </summary>
public interface ILayoutRepository
{
    /// <summary>
    /// Loads a layout by identifier, **within the fabs the caller holds**
    /// (spec 017 FR-006).
    ///
    /// <para>
    /// The fabs are part of the lookup rather than a check the caller makes
    /// afterwards, and that is the point: a layout in another fab and one that
    /// never existed leave here identically, so every write built on this
    /// answers "not found" for both. It also fixes the ordering FR-006 needs —
    /// the fab is applied before any precondition can be read, so a stale-version
    /// or missing-revision answer can never be given for a layout the caller
    /// was not entitled to address.
    /// </para>
    /// </summary>
    Task<Option<Layout>> GetByIdentifierAsync(
        IReadOnlyList<FabIdentifier> fabs, LayoutIdentifier layout, CancellationToken cancellationToken);

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
