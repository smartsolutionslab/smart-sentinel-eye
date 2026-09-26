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

    public IReadOnlyList<Wall> Walls => walls;

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
        foreach (Wall wall in walls)
        {
            wall.ClearPendingEvents();
        }
        return Task.CompletedTask;
    }
}
