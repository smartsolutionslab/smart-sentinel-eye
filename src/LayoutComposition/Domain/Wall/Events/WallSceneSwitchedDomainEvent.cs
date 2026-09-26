using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall.Events;

/// <summary>
/// In-process domain event raised when a <see cref="Wall"/>'s
/// <c>Showing</c> scene actually changes (spec 258 FR-004). A no-op switch
/// raises nothing. Translated to <c>WallSceneChangedV1</c> and a
/// <c>WallSceneChanged</c> SignalR frame by the Application layer.
/// </summary>
public sealed record WallSceneSwitchedDomainEvent(
    FabIdentifier Fab,
    WallIdentifier Wall,
    LayoutIdentifier Previous,
    LayoutIdentifier Current,
    SceneVersion SceneVersion,
    SceneSwitchCause Cause,
    DateTimeOffset At) : IDomainEvent;
