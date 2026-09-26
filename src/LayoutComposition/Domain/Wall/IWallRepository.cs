using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall;

/// <summary>
/// Wall repository contract (ADR-0041, spec 258 PD-2). Implementation lives
/// in LayoutComposition.Infrastructure; the Domain layer has no persistence
/// dependency. Follows <see cref="ILayoutRepository"/>'s conventions
/// exactly: the fab is part of every lookup, never a check made afterwards
/// (FR-006's pattern, reused for walls).
/// </summary>
public interface IWallRepository
{
    /// <summary>
    /// Loads a wall by identifier, within the fabs the caller holds. A wall
    /// in another fab and one that never existed leave here identically
    /// (US1-14).
    /// </summary>
    Task<Option<Wall>> FindAsync(
        WallIdentifier wall, IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken);

    /// <summary>
    /// Looks a wall up by name within one fab (FR-001's uniqueness rule). A
    /// name held in another fab is invisible here — mirrors
    /// <see cref="ILayoutRepository.GetByNameAsync"/>.
    /// </summary>
    Task<Option<Wall>> FindByNameAsync(FabIdentifier fab, WallName name, CancellationToken cancellationToken);

    /// <summary>Every wall in the given fabs (FR-005's read-side pattern, reused for walls).</summary>
    Task<IReadOnlyList<Wall>> ListAsync(IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken);

    void Add(Wall wall);

    Task SaveAsync(CancellationToken cancellationToken);
}
