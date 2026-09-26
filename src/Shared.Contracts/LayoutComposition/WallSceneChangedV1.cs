namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event raised whenever a <c>Wall</c>'s <c>Showing</c> scene
/// actually changes (spec 258 US1/US2, ADR-0157). A switch that leaves
/// <c>Showing</c> unchanged raises nothing (FR-004).
///
/// <para>
/// <paramref name="Cause"/> is one of <c>"Operator"</c>, <c>"Rule"</c> or
/// <c>"Reconfigured"</c>. <paramref name="Rule"/> and
/// <paramref name="CausingEventIdentifier"/> are set if and only if
/// <paramref name="Cause"/> is <c>"Rule"</c> — a flat shape rather than a
/// polymorphic payload, matching <c>RuleActionDto</c>'s own convention
/// (plan.md §1). <see cref="EventMetadata.Actor"/> names the operator for
/// <c>"Operator"</c>/<c>"Reconfigured"</c> and is <see langword="null"/> for
/// <c>"Rule"</c>.
/// </para>
/// </summary>
public sealed record WallSceneChangedV1(
    Guid Wall,
    Guid PreviousLayout,
    Guid CurrentLayout,
    long SceneVersion,
    string Cause,
    Guid? Rule,
    Guid? CausingEventIdentifier,
    DateTimeOffset ChangedAt,
    EventMetadata Metadata) : IIntegrationEvent;
