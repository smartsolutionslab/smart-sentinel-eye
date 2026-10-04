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
    public async Task Handler_publishes_the_V2_integration_event()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        Label label = Label.From("Hello", NormalizedPosition.From(0.2m, 0.3m), NormalizedSize.From(0.4m, 0.5m), 32);
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), [label], FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        OverlayRevisionPublishedV2 v2 = bus.Published.OfType<OverlayRevisionPublishedV2>().ShouldHaveSingleItem();
        v2.Overlay.ShouldBe(overlayId.Value);
        v2.RevisionNumber.ShouldBe(1);
        OverlayLabelV2 wire = v2.Labels.ShouldHaveSingleItem();
        wire.Text.ShouldBe(label.Text);
        wire.NormalizedX.ShouldBe(label.Position.X);
        wire.NormalizedY.ShouldBe(label.Position.Y);
        wire.NormalizedWidth.ShouldBe(label.Size.Width);
        wire.NormalizedHeight.ShouldBe(label.Size.Height);
        wire.FontSizePx.ShouldBe(label.FontSizePx);
        v2.PublishedBy.ShouldBe(by.Value);
    }

    [Fact]
    public async Task Three_labels_are_published_as_one_event_carrying_all_three_in_order()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        Label first = Label.From("First", NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.2m, 0.2m), 16);
        Label second = Label.From("Second", NormalizedPosition.From(0.2m, 0.2m), NormalizedSize.From(0.2m, 0.2m), 20);
        Label third = Label.From("Third", NormalizedPosition.From(0.3m, 0.3m), NormalizedSize.From(0.2m, 0.2m), 24);
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), [first, second, third], FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        List<OverlayRevisionPublishedV2> published = [.. bus.Published.OfType<OverlayRevisionPublishedV2>()];
        published.Count.ShouldBe(1);
        published[0].Labels.Count.ShouldBe(3);
        published[0].Labels[0].Text.ShouldBe("First");
        published[0].Labels[1].Text.ShouldBe("Second");
        published[0].Labels[2].Text.ShouldBe("Third");
    }
}
