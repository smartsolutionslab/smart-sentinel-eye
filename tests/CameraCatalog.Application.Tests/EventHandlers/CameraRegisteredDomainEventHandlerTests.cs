using System.Globalization;
using SmartSentinelEye.CameraCatalog.Application.EventHandlers;
using SmartSentinelEye.CameraCatalog.Application.Tests.Fakes;
using SmartSentinelEye.CameraCatalog.Domain.Camera;
using SmartSentinelEye.CameraCatalog.Domain.Camera.Events;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.CameraCatalog.Application.Tests.EventHandlers;

/// <summary>
/// Spec 015 T031 — the fab travels on the integration event (FR-012).
///
/// <para>
/// Without it, every subscriber that needs to know which plant a camera belongs
/// to must call back into this context per camera. StreamDistribution's own fab
/// scoping is the next feature and would otherwise begin by adding this.
/// </para>
/// </summary>
public class CameraRegisteredDomainEventHandlerTests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-05-25T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_published_event_carries_the_cameras_fab()
    {
        FakeEventBus bus = new();
        CameraRegisteredDomainEventHandler handler = new(bus);

        await handler.Handle(EventFor("dresden", "Line-1-North"), CancellationToken.None);

        CameraRegisteredV1 published = bus.Published.OfType<CameraRegisteredV1>().ShouldHaveSingleItem();

        // dresden, not munich: everything else in the system defaults to
        // munich, so a handler that hard-coded it would pass otherwise.
        published.Metadata.Fab.ShouldBe("dresden");
    }

    [Fact]
    public async Task The_fab_rides_the_metadata_rather_than_the_body()
    {
        // Additive by design: EventMetadata.Fab already existed and was stamped
        // null, so no consumer has to change and no V2 is needed (ADR-0073). A
        // first-class field on the body would have forced every subscriber to
        // migrate for something they can already read.
        FakeEventBus bus = new();
        CameraRegisteredDomainEventHandler handler = new(bus);

        await handler.Handle(EventFor("munich", "Line-2-East"), CancellationToken.None);

        CameraRegisteredV1 published = bus.Published.OfType<CameraRegisteredV1>().ShouldHaveSingleItem();

        published.Metadata.Fab.ShouldBe("munich");
        typeof(CameraRegisteredV1).GetProperty("Fab").ShouldBeNull();
    }

    private static CameraRegisteredDomainEvent EventFor(string fab, string name) =>
        new(
            Camera: CameraIdentifier.New(),
            Fab: FabIdentifier.From(fab),
            Name: CameraName.From(name),
            Url: RtspUrl.From("rtsp://10.0.5.12/h264"),
            RegisteredAt: Moment,
            RegisteredBy: OperatorIdentifier.From(Guid.CreateVersion7()));
}
