using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Domain.Wall.Events;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

/// <summary>
/// Spec 258 US1 (T011), plan.md §3. Mirrors
/// <c>LayoutRevisionPublishedDomainEventHandlerTests</c>: the domain event
/// -&gt; integration event mapping, kept out of the command-handler tests
/// exactly as <c>PublishRevisionCommandHandlerTests</c> does not itself build
/// <c>LayoutRevisionPublishedV1</c>.
/// </summary>
public class WallConfiguredDomainEventHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-09-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Handle_publishes_WallConfiguredV1_with_the_ordered_scene_list()
    {
        FakeEventBus bus = new();
        WallConfiguredDomainEventHandler handler = new(bus);

        WallIdentifier wall = WallIdentifier.New();
        LayoutIdentifier a = LayoutIdentifier.New();
        LayoutIdentifier b = LayoutIdentifier.New();
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        WallConfiguredDomainEvent domainEvent = new(
            Munich, wall, WallName.From("Line 3 rotation"), [a, b], a, FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        bus.Published.ShouldHaveSingleItem();
        WallConfiguredV1 v1 = bus.Published.Single().ShouldBeOfType<WallConfiguredV1>();
        v1.Wall.ShouldBe(wall.Value);
        v1.Name.ShouldBe("Line 3 rotation");
        v1.Scenes.ShouldBe([a.Value, b.Value]);
        v1.Showing.ShouldBe(a.Value);
        v1.ConfiguredAt.ShouldBe(FixedMoment);
    }

    [Fact]
    public async Task Stamps_the_walls_own_fab_on_the_published_event()
    {
        FakeEventBus bus = new();
        WallConfiguredDomainEventHandler handler = new(bus);
        WallConfiguredDomainEvent domainEvent = new(
            Munich, WallIdentifier.New(), WallName.From("Line 3 rotation"), [LayoutIdentifier.New(), LayoutIdentifier.New()],
            LayoutIdentifier.New(), FixedMoment, OperatorIdentifier.From(Guid.CreateVersion7()));

        await handler.Handle(domainEvent, CancellationToken.None);

        WallConfiguredV1 v1 = bus.Published.Single().ShouldBeOfType<WallConfiguredV1>();
        v1.Metadata.Fab.ShouldBe("munich");
    }
}
