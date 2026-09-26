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
/// <see cref="WallSceneChangedV1"/> (spec 258 US1/US2, plan.md §4.3). FR-006:
/// for an <see cref="SceneSwitchCause.Operator"/> or
/// <see cref="SceneSwitchCause.Reconfigured"/> cause,
/// <see cref="EventMetadata.Actor"/> names the operator; for
/// <see cref="SceneSwitchCause.Rule"/> it is <see langword="null"/> and the
/// rule + causing-event identifiers are carried instead.
///
/// <para>
/// Deliberately does <b>not</b> call <see cref="ILayoutLifecycleBroadcaster"/>
/// itself (phase-6 remediation): this handler runs pre-commit, alongside
/// <c>WallRepository.SaveAsync</c>'s domain-event dispatch, so a broadcast
/// from here could reach a kiosk for a write whose transaction goes on to
/// lose a concurrency conflict and never commits. The hub push lives in
/// <see cref="WallSceneChangedV1Handler"/> instead, a Wolverine subscriber on
/// the integration event this handler publishes — which only runs once the
/// outbox actually releases the message, i.e. after the commit succeeds.
/// </para>
/// </summary>
public sealed class WallSceneSwitchedDomainEventHandler(IEventBus events)
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
