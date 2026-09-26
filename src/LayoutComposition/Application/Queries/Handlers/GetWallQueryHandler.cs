using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries.Handlers;

public sealed class GetWallQueryHandler(IWallRepository walls)
    : IQueryHandler<GetWallQuery, Result<WallDto, GetWallError>>
{
    public async Task<Result<WallDto, GetWallError>> HandleAsync(
        GetWallQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        (IReadOnlyList<FabIdentifier> fabs, WallIdentifier wall) = query;

        // US1-14: the fab is part of the lookup rather than a check
        // afterwards, so a wall outside the caller's fabs and one that
        // never existed take the same path out of here.
        Option<Wall> found = await walls.FindAsync(wall, fabs, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(GetWallFailures.WallNotFound(wall.Value));
        }

        return Success(Map(found.Value));
    }

    internal static WallDto Map(Wall wall) =>
        new(
            Wall: wall.Id.Value,
            Version: wall.Version,
            Fab: wall.Fab.Value,
            Name: wall.Name.Value,
            Scenes: [.. wall.Scenes.Select(scene => scene.Value)],
            Showing: wall.Showing.Value,
            SceneVersion: wall.SceneVersion.Value,
            ShowingSince: wall.ShowingSince.Value);
}
