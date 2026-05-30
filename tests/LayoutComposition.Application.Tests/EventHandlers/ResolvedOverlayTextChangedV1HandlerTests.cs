using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class ResolvedOverlayTextChangedV1HandlerTests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Relays_the_resolved_overlay_text_onto_the_broadcaster()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        ResolvedOverlayTextChangedV1Handler handler = new(
            broadcaster, NullLogger<ResolvedOverlayTextChangedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        ResolvedOverlayTextChangedV1 message = new(
            Overlay: overlay,
            ResolvedText: "OEE: 82.5%",
            Version: 7,
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        var notification = broadcaster.ResolvedTextChanged.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.ResolvedText.ShouldBe("OEE: 82.5%");
        notification.Version.ShouldBe(7);
    }
}
