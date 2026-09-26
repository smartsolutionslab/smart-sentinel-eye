using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// A manual switch through management-web (spec 258 US1, PD-4). Requires
/// <see cref="ExpectedVersion"/> (ADR-0113); a switch that leaves
/// <c>Showing</c> unchanged is a no-op (US1-6).
/// </summary>
public sealed record SwitchWallSceneCommand(
    IReadOnlyList<FabIdentifier> Fabs,
    WallIdentifier Wall,
    int ExpectedVersion,
    SceneTarget Target,
    OperatorIdentifier By)
    : ICommand<Result<WallDto, SwitchWallSceneError>>;
