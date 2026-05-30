using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.EventHandlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Handler_publishes_the_V1_integration_event()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        Label label = Label.From("Hello", 0.2m, 0.3m, 0.4m, 0.5m, 32);
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), label, FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        OverlayRevisionPublishedV1 v1 = bus.Published.OfType<OverlayRevisionPublishedV1>().ShouldHaveSingleItem();
        v1.Overlay.ShouldBe(overlayId.Value);
        v1.RevisionNumber.ShouldBe(1);
        v1.Text.ShouldBe(label.Text);
        v1.NormalizedX.ShouldBe(label.NormalizedX);
        v1.NormalizedHeight.ShouldBe(label.NormalizedHeight);
        v1.FontSizePx.ShouldBe(label.FontSizePx);
        v1.PublishedBy.ShouldBe(by.Value);
    }
}
