using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Domain.Wall.Events;

/// <summary>
/// In-process domain event raised when a <see cref="Wall"/> is created or
/// its scene set is edited (spec 258 US1, FR-001..003). Translated to
/// <c>WallConfiguredV1</c> on the integration bus by the Application layer.
/// </summary>
public sealed record WallConfiguredDomainEvent(
    FabIdentifier Fab,
    WallIdentifier Wall,
    WallName Name,
    IReadOnlyList<LayoutIdentifier> Scenes,
    LayoutIdentifier Showing,
    DateTimeOffset ConfiguredAt,
    OperatorIdentifier By) : IDomainEvent;
