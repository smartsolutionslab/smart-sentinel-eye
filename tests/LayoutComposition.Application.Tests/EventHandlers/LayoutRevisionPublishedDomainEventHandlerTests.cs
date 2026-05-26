using System.Globalization;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Layout.Events;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class LayoutRevisionPublishedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-26T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Handle_publishes_integration_event_and_broadcasts_notification()
    {
        FakeEventBus bus = new();
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        LayoutRevisionPublishedDomainEventHandler handler = new(bus, broadcaster);

        LayoutIdentifier layout = LayoutIdentifier.New();
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        LayoutRevisionPublishedDomainEvent domainEvent = new(
            layout, LayoutRevisionNumber.One, LayoutName.From("Line-1"), camera, FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        bus.Published.ShouldHaveSingleItem();
        LayoutRevisionPublishedV1 v1 = bus.Published.Single().ShouldBeOfType<LayoutRevisionPublishedV1>();
        v1.Layout.ShouldBe(layout.Value);
        v1.RevisionNumber.ShouldBe(1);
        v1.Name.ShouldBe("Line-1");
        v1.Camera.ShouldBe(camera.Value);
        v1.PublishedAt.ShouldBe(FixedMoment);
        v1.PublishedBy.ShouldBe(by.Value);

        broadcaster.Published.ShouldHaveSingleItem();
        broadcaster.Published.Single().Layout.ShouldBe(layout);
    }
}
