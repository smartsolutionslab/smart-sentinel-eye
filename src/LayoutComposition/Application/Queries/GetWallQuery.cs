using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds. A wall outside them is reported
/// as not found rather than forbidden (US1-14) — the caller addressed a
/// wall, so "forbidden" would confirm it exists.
/// </summary>
public sealed record GetWallQuery(IReadOnlyList<FabIdentifier> Fabs, WallIdentifier Wall)
    : IQuery<Result<WallDto, GetWallError>>;
