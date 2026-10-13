using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Application.EventHandlers;
using SmartSentinelEye.OverlayDesigner.Application.Tests.Fakes;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay.Events;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Tests.EventHandlers;

public class OverlayRevisionArchivedDomainEventHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Handler_publishes_the_V1_integration_event()
    {
        FakeEventBus bus = new();
        OverlayRevisionArchivedDomainEventHandler handler = new(bus);

        var overlayId = OverlayIdentifier.From(Guid.CreateVersion7());
        var by = OperatorIdentifier.From(Guid.CreateVersion7());
        OverlayRevisionArchivedDomainEvent domainEvent = new(
            overlayId, OverlayRevisionNumber.One, FixedMoment, by);

        await handler.Handle(domainEvent, CancellationToken.None);

        var v1 = bus.Published.OfType<OverlayRevisionArchivedV1>().ShouldHaveSingleItem();
        v1.Overlay.ShouldBe(overlayId.Value);
        v1.RevisionNumber.ShouldBe(1);
        v1.ArchivedAt.ShouldBe(FixedMoment);
        v1.ArchivedBy.ShouldBe(by.Value);
    }
}
