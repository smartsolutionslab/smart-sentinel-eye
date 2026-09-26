using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Translates <see cref="WallSceneSwitchedDomainEvent"/> into
/// <see cref="WallSceneChangedV1"/> AND a <c>WallSceneChanged</c> SignalR
/// frame (spec 258 US1/US2, plan.md §4.3). FR-006: for an
/// <see cref="SceneSwitchCause.Operator"/> or
/// <see cref="SceneSwitchCause.Reconfigured"/> cause,
/// <see cref="EventMetadata.Actor"/> names the operator; for
/// <see cref="SceneSwitchCause.Rule"/> it is <see langword="null"/> and the
/// rule + causing-event identifiers are carried instead.
/// </summary>
public sealed class WallSceneSwitchedDomainEventHandler(
    IEventBus events,
    ILayoutLifecycleBroadcaster broadcaster)
    : IDomainEventHandler<WallSceneSwitchedDomainEvent>
{
    public async Task Handle(WallSceneSwitchedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (fab, wall, previous, current, sceneVersion, cause, at) = domainEvent;
        (string label, Guid? actor, Guid? rule, Guid? causingEvent) = Describe(cause);

        await events.PublishAsync(
            new WallSceneChangedV1(
                Wall: wall.Value,
                PreviousLayout: previous.Value,
                CurrentLayout: current.Value,
                SceneVersion: sceneVersion.Value,
                Cause: label,
                Rule: rule,
                CausingEventIdentifier: causingEvent,
                ChangedAt: at,
                Metadata: new EventMetadata(Guid.CreateVersion7(), at, fab.Value, actor)),
            cancellationToken);

        await broadcaster.WallSceneChangedAsync(
            new WallSceneChangedNotification(fab, wall, current, sceneVersion),
            cancellationToken);
    }

    private static (string Label, Guid? Actor, Guid? Rule, Guid? CausingEvent) Describe(SceneSwitchCause cause) =>
        cause switch
        {
            SceneSwitchCause.Operator operatorCause => ("Operator", operatorCause.By.Value, null, null),
            SceneSwitchCause.Reconfigured reconfigured => ("Reconfigured", reconfigured.By.Value, null, null),
            SceneSwitchCause.Rule rule => ("Rule", null, rule.RuleIdentifier.Value, rule.CausingEventIdentifier.Value),
            _ => throw new InvalidOperationException($"Unknown SceneSwitchCause '{cause}'."),
        };
}
