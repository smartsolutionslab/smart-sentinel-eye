using SmartSentinelEye.LayoutComposition.Application.DTOs;
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

        (var fabs, var wall) = query;

        // US1-14: the fab is part of the lookup rather than a check
        // afterwards, so a wall outside the caller's fabs and one that
        // never existed take the same path out of here.
        var found = await walls.FindAsync(wall, fabs, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(GetWallFailures.WallNotFound(wall.Value));
        }

        return Success(found.Value.ToDto());
    }
}
