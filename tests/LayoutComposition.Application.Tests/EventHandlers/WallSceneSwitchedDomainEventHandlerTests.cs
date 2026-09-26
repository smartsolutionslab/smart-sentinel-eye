using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Spec 258 US1 (T011), plan.md §3/§4.3. Maps
/// <c>WallSceneSwitchedDomainEvent</c> to <c>WallSceneChangedV1</c> AND
/// broadcasts <c>WallSceneChangedAsync</c> — FR-006's
/// "<c>Metadata.Actor</c> names the operator" for an Operator/Reconfigured
/// cause, and plan.md §4.3's hub notification.
/// </summary>
public class WallSceneSwitchedDomainEventHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task An_Operator_switch_publishes_WallSceneChangedV1_with_Metadata_Actor_set_to_the_operator()
    {
        FakeEventBus bus = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        WallSceneSwitchedDomainEventHandler handler = new(bus, broadcaster);

        WallIdentifier wall = WallIdentifier.New();
        LayoutIdentifier previous = LayoutIdentifier.New();
        LayoutIdentifier current = LayoutIdentifier.New();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        WallSceneSwitchedDomainEvent domainEvent = new(
            Munich, wall, previous, current, SceneVersion.Initial.Next(),
            new SceneSwitchCause.Operator(by), FixedMoment);

        await handler.Handle(domainEvent, CancellationToken.None);

        bus.Published.ShouldHaveSingleItem();
        WallSceneChangedV1 v1 = bus.Published.Single().ShouldBeOfType<WallSceneChangedV1>();
        v1.Wall.ShouldBe(wall.Value);
        v1.PreviousLayout.ShouldBe(previous.Value);
        v1.CurrentLayout.ShouldBe(current.Value);
        v1.SceneVersion.ShouldBe(1L);
        v1.Cause.ShouldBe("Operator");
        v1.Rule.ShouldBeNull();
        v1.CausingEventIdentifier.ShouldBeNull();
        v1.Metadata.Actor.ShouldBe(by.Value);
        v1.Metadata.Fab.ShouldBe("munich");
    }

    [Fact]
    public async Task A_Reconfigured_switch_publishes_Cause_Reconfigured_with_Metadata_Actor_set_to_the_editor()
    {
        FakeEventBus bus = new();
        WallSceneSwitchedDomainEventHandler handler = new(bus, new FakeLayoutLifecycleBroadcaster());
        OperatorIdentifier editor = OperatorIdentifier.From(Guid.CreateVersion7());
        WallSceneSwitchedDomainEvent domainEvent = new(
            Munich, WallIdentifier.New(), LayoutIdentifier.New(), LayoutIdentifier.New(), SceneVersion.Initial.Next(),
            new SceneSwitchCause.Reconfigured(editor), FixedMoment);

        await handler.Handle(domainEvent, CancellationToken.None);

        WallSceneChangedV1 v1 = bus.Published.Single().ShouldBeOfType<WallSceneChangedV1>();
        v1.Cause.ShouldBe("Reconfigured");
        v1.Rule.ShouldBeNull();
        v1.CausingEventIdentifier.ShouldBeNull();
        v1.Metadata.Actor.ShouldBe(editor.Value);
    }

    [Fact]
    public async Task Broadcasts_the_new_Showing_scene_and_SceneVersion_over_the_hub()
    {
        FakeEventBus bus = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        WallSceneSwitchedDomainEventHandler handler = new(bus, broadcaster);
        WallIdentifier wall = WallIdentifier.New();
        LayoutIdentifier current = LayoutIdentifier.New();
        WallSceneSwitchedDomainEvent domainEvent = new(
            Munich, wall, LayoutIdentifier.New(), current, SceneVersion.Initial.Next(),
            new SceneSwitchCause.Operator(OperatorIdentifier.From(Guid.CreateVersion7())), FixedMoment);

        await handler.Handle(domainEvent, CancellationToken.None);

        broadcaster.WallSceneChanged.ShouldHaveSingleItem();
        WallSceneChangedNotification notification = broadcaster.WallSceneChanged.Single();
        notification.Fab.ShouldBe(Munich);
        notification.Wall.ShouldBe(wall);
        notification.Showing.ShouldBe(current);
        notification.SceneVersion.ShouldBe(SceneVersion.Initial.Next());
    }
}
