using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayHighlightRequestedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Calls_broadcaster_OverlayHighlightedAsync_with_the_overlay_and_duration()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayHighlightRequestedV1Handler handler = new(
            broadcaster, NullLogger<OverlayHighlightRequestedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayHighlightRequestedV1 message = new(overlay, 10_000, Moment, Guid.CreateVersion7());

        await handler.Handle(message, CancellationToken.None);

        var notification = broadcaster.Highlighted.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.DurationMs.ShouldBe(10_000);
    }
}
