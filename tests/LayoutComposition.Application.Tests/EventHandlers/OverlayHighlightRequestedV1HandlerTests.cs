using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayHighlightRequestedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Calls_broadcaster_OverlayHighlightedAsync_with_the_overlay_and_duration()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayHighlightRequestedV1 message = new(overlay, 10_000, Moment, Guid.CreateVersion7(), Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        var notification = broadcaster.Highlighted.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.DurationMs.ShouldBe(10_000);
    }
}
