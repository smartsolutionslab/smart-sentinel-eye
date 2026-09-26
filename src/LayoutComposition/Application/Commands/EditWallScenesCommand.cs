using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Commands;

/// <summary>
/// Replaces a <see cref="Wall"/>'s scene set (spec 258 US1-15). If the
/// scene currently showing is dropped, the pointer moves to the new first
/// scene and the switch is attributed to <paramref name="By"/> with
/// <see cref="SceneSwitchCause.Reconfigured"/>.
/// </summary>
public sealed record EditWallScenesCommand(
    IReadOnlyList<FabIdentifier> Fabs,
    WallIdentifier Wall,
    int ExpectedVersion,
    IReadOnlyList<LayoutIdentifier> Scenes,
    OperatorIdentifier By)
    : ICommand<Result<WallDto, EditWallScenesError>>;
