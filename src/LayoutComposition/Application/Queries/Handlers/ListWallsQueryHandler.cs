using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class ListWallsQueryHandler(IWallRepository walls)
    : IQueryHandler<ListWallsQuery, IReadOnlyList<WallDto>>
{
    public async Task<IReadOnlyList<WallDto>> HandleAsync(
        ListWallsQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        // FR-005's read-side pattern, reused for walls: only walls in fabs
        // the caller holds.
        IReadOnlyList<Wall> found = await walls.ListAsync(query.Fabs, cancellationToken);

        return [.. found.Select(GetWallQueryHandler.Map)];
    }
}
