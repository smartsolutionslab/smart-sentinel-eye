using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.LayoutComposition.Application.EventHandlers;
using SmartSentinelEye.LayoutComposition.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;

namespace SmartSentinelEye.LayoutComposition.Application.Tests.EventHandlers;

public class OverlayRevisionArchivedV1HandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public async Task Relays_the_overlay_archive_onto_the_broadcaster()
    {
        FakeLayoutLifecycleBroadcaster broadcaster = new();
        OverlayRevisionArchivedV1Handler handler = new(
            broadcaster, NullLogger<OverlayRevisionArchivedV1Handler>.Instance);

        Guid overlay = Guid.CreateVersion7();
        OverlayRevisionArchivedV1 message = new(
            Overlay: overlay,
            RevisionNumber: 2,
            ArchivedAt: Moment,
            ArchivedBy: Guid.CreateVersion7(),
            Metadata: TestMetadata);

        await handler.Handle(message, CancellationToken.None);

        var notification = broadcaster.OverlaysArchived.ShouldHaveSingleItem();
        notification.Overlay.ShouldBe(overlay);
        notification.RevisionNumber.ShouldBe(2);
        notification.ArchivedAt.ShouldBe(Moment);
    }
}
