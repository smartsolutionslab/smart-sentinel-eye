using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

/// <summary>
/// Lists every wall in the caller's fabs (spec 258 US1). Unlike a write,
/// nothing has to be chosen: a multi-fab caller sees all of theirs
/// (FR-005's read-side pattern, reused for walls). Reads directly through
/// <c>IWallRepository</c> rather than a Result-wrapped error type — a list
/// has nothing to fail on beyond an empty answer.
/// </summary>
public sealed record ListWallsQuery(IReadOnlyList<FabIdentifier> Fabs)
    : IQuery<IReadOnlyList<WallDto>>;
