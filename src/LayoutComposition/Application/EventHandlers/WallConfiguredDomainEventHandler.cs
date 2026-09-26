using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Translates <see cref="WallConfiguredDomainEvent"/> into
/// <see cref="WallConfiguredV1"/> (spec 258 US1). No SignalR broadcast: a
/// picker or detail page re-reads on demand, and only a scene
/// <em>switch</em> (<see cref="WallSceneSwitchedDomainEventHandler"/>) needs
/// a live push.
/// </summary>
public sealed class WallConfiguredDomainEventHandler(IEventBus events)
    : IDomainEventHandler<WallConfiguredDomainEvent>
{
    public async Task Handle(WallConfiguredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (fab, wall, name, scenes, showing, configuredAt, by) = domainEvent;

        await events.PublishAsync(
            new WallConfiguredV1(
                Wall: wall.Value,
                Name: name.Value,
                Scenes: [.. scenes.Select(scene => scene.Value)],
                Showing: showing.Value,
                ConfiguredAt: configuredAt,
                Metadata: new EventMetadata(Guid.CreateVersion7(), configuredAt, fab.Value, by.Value)),
            cancellationToken);
    }
}
