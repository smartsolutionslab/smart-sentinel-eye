using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayRevisionPublishedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Relays_the_overlay_publish_onto_the_broadcaster_with_every_field_mapped()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayRevisionPublishedV1Handler handler = new(
            broadcaster, NullLogger<OverlayRevisionPublishedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionPublishedV1 message = new(
            Overlay: overlay,
            RevisionNumber: 3,
            Name: "Line-1",
            Text: "Hello",
            NormalizedX: 0.2m,
            NormalizedY: 0.3m,
            NormalizedWidth: 0.4m,
            NormalizedHeight: 0.5m,
            FontSizePx: 32,
            PublishedAt: Moment,
            PublishedBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        var notification = broadcaster.OverlaysPublished.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(3);
        notification.Name.ShouldBe("Line-1");
        notification.Text.ShouldBe("Hello");
        notification.NormalizedX.ShouldBe(0.2m);
        notification.NormalizedHeight.ShouldBe(0.5m);
        notification.FontSizePx.ShouldBe(32);
        notification.PublishedAt.ShouldBe(Moment);
    }
}
