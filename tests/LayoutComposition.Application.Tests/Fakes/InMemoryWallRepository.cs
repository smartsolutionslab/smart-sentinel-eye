using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;

/// <summary>
/// In-memory <c>IWallRepository</c> for handler tests (spec 258 US1, plan.md
/// §2). Mirrors <see cref="InMemoryLayoutRepository"/> exactly: fab is part
/// of every lookup (FR-006's pattern, reused for walls), and
/// <c>SaveAsync</c> clears pending events to mimic the real
/// dispatcher-after-Save flow.
/// </summary>
public sealed class InMemoryWallRepository : IWallRepository
{
    private readonly List<Wall> walls = [];
    private Exception? saveException;

    public IReadOnlyList<Wall> Walls => walls;

    /// <summary>
    /// Spec 296 plan.md §3.2: the unscoped existence probe FR-010 (b)/(c)
    /// needs for the log line — not scoped to a caller's fabs, because
    /// nothing here crosses an HTTP boundary.
    /// </summary>
    public Task<Option<FabIdentifier>> FindFabAsync(WallIdentifier wall, CancellationToken cancellationToken)
    {
        Wall? found = walls.SingleOrDefault(candidate => candidate.Id == wall);
        return Task.FromResult(found is null ? Option<FabIdentifier>.None : Option<FabIdentifier>.Some(found.Fab));
    }

    /// <summary>
    /// Spec 296 FR-011: makes the next <see cref="SaveAsync"/> throw instead
    /// of committing, so a handler test can assert the exception escapes
    /// rather than being caught and swallowed.
    /// </summary>
    public void FailNextSaveWith(Exception exception) => saveException = exception;

    public Task<Option<Wall>> FindAsync(
        WallIdentifier wall, IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        Ensure.That(fabs).IsNotNull();
        Wall? found = walls.SingleOrDefault(candidate => candidate.Id == wall && fabs.Contains(candidate.Fab));
        return Task.FromResult(found is null ? Option<Wall>.None : Option<Wall>.Some(found));
    }

    public Task<Option<Wall>> FindByNameAsync(
        FabIdentifier fab, WallName name, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();
        Ensure.That(name).IsNotNull();
        Wall? found = walls.SingleOrDefault(candidate => candidate.Fab == fab && candidate.Name == name);
        return Task.FromResult(found is null ? Option<Wall>.None : Option<Wall>.Some(found));
    }

    public Task<IReadOnlyList<Wall>> ListAsync(IReadOnlyList<FabIdentifier> fabs, CancellationToken cancellationToken)
    {
        Ensure.That(fabs).IsNotNull();
        IReadOnlyList<Wall> result = [.. walls.Where(candidate => fabs.Contains(candidate.Fab))];
        return Task.FromResult(result);
    }

    public void Add(Wall wall)
    {
        Ensure.That(wall).IsNotNull();
        walls.Add(wall);
    }

    public Task SaveAsync(CancellationToken cancellationToken)
    {
        if (saveException is { } exception)
        {
            saveException = null;
            throw exception;
        }

        foreach (Wall wall in walls)
        {
            wall.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
