using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.EventHandlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.EventHandlers;

/// <summary>
/// Spec 403 (#3008) — the published <see cref="CameraAddressChangedV1"/> carries
/// the version the change was made against, as the plain integer the wire
/// vocabulary uses (ADR-0040).
/// </summary>
public class CameraAddressChangedDomainEventHandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(1234)]
    public async Task The_published_event_carries_the_version_the_change_was_made_against(int version)
    {
        FakeEventBus bus = new();
        CameraAddressChangedDomainEventHandler handler = new(bus);

        await handler.Handle(EventAt(version), CancellationToken.None);

        var published = bus.Published.OfType<CameraAddressChangedV1>().ShouldHaveSingleItem();
        published.PreviousVersion.ShouldBe(version);
    }

    [Fact]
    public async Task The_rest_of_the_published_event_is_unchanged()
    {
        FakeEventBus bus = new();
        CameraAddressChangedDomainEventHandler handler = new(bus);
        var domainEvent = EventAt(5);

        await handler.Handle(domainEvent, CancellationToken.None);

        var published = bus.Published.OfType<CameraAddressChangedV1>().ShouldHaveSingleItem();
        published.Camera.ShouldBe(domainEvent.Camera.Value);
        published.Fab.ShouldBe("munich");
        published.PreviousUrl.ShouldBe("rtsp://10.0.5.12/h264");
        published.Url.ShouldBe("rtsp://10.0.5.44/h264");
        published.ChangedAt.ShouldBe(Moment);
        published.ChangedBy.ShouldBe(domainEvent.ChangedBy.Value);
    }

    private static CameraAddressChangedDomainEvent EventAt(int version) =>
        new(
            Camera: CameraIdentifier.New(),
            Fab: FabIdentifier.From("munich"),
            PreviousUrl: RtspUrl.From("rtsp://10.0.5.12/h264"),
            Url: RtspUrl.From("rtsp://10.0.5.44/h264"),
            ChangedAt: Moment,
            ChangedBy: OperatorIdentifier.From(Guid.CreateVersion7()),
            PreviousVersion: AggregateVersion.From(version));
}
