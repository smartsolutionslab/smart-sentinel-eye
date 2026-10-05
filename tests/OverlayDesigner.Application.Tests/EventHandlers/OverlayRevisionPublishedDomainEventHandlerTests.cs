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
    public async Task Handler_publishes_the_V3_integration_event()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        OverlayElement label = OverlayElement.TextElement("Hello", 32, NormalizedPosition.From(0.2m, 0.3m), NormalizedSize.From(0.4m, 0.5m), OverlayColor.Default);
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), [label], FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        OverlayRevisionPublishedV3 v3 = bus.Published.OfType<OverlayRevisionPublishedV3>().ShouldHaveSingleItem();
        v3.Overlay.ShouldBe(overlayId.Value);
        v3.RevisionNumber.ShouldBe(1);
        OverlayElementV3 wire = v3.Elements.ShouldHaveSingleItem();
        wire.Kind.ShouldBe("Text");
        wire.Color.ShouldBe(label.Color.Value);
        wire.Text.ShouldBe(label.Text!.Value);
        wire.NormalizedX.ShouldBe(label.Position.X);
        wire.NormalizedY.ShouldBe(label.Position.Y);
        wire.NormalizedWidth.ShouldBe(label.Size.Width);
        wire.NormalizedHeight.ShouldBe(label.Size.Height);
        wire.FontSizePx.ShouldBe(label.Text.FontSizePx);
        v3.PublishedBy.ShouldBe(by.Value);
    }

    [Fact]
    public async Task Three_elements_are_published_as_one_event_carrying_all_three_in_order()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        OverlayElement first = OverlayElement.TextElement("First", 16, NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);
        OverlayElement second = OverlayElement.TextElement("Second", 20, NormalizedPosition.From(0.2m, 0.2m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);
        OverlayElement third = OverlayElement.TextElement("Third", 24, NormalizedPosition.From(0.3m, 0.3m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.Default);
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), [first, second, third], FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        List<OverlayRevisionPublishedV3> published = [.. bus.Published.OfType<OverlayRevisionPublishedV3>()];
        published.Count.ShouldBe(1);
        published[0].Elements.Count.ShouldBe(3);
        published[0].Elements[0].Text.ShouldBe("First");
        published[0].Elements[1].Text.ShouldBe("Second");
        published[0].Elements[2].Text.ShouldBe("Third");
    }

    [Fact]
    public async Task A_non_text_element_contributes_null_text_and_font_size()
    {
        FakeEventBus bus = new();
        OverlayRevisionPublishedDomainEventHandler handler = new(bus);

        OverlayElement box = OverlayElement.Box(NormalizedPosition.From(0.1m, 0.1m), NormalizedSize.From(0.2m, 0.2m), OverlayColor.From("#D32F2F00"));
        OverlayIdentifier overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        OperatorIdentifier by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionPublishedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One,
            OverlayName.From("Line-1"), [box], FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        OverlayElementV3 wire = bus.Published.OfType<OverlayRevisionPublishedV3>().ShouldHaveSingleItem().Elements.ShouldHaveSingleItem();
        wire.Kind.ShouldBe("Box");
        wire.Text.ShouldBeNull();
        wire.FontSizePx.ShouldBeNull();
    }
}
