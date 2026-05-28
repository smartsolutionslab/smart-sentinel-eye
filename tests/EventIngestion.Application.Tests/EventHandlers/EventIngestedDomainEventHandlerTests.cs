using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

public class EventIngestedDomainEventHandlerTests
{
    private static readonly DateTimeOffset OccurredAtMoment =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset IngestedAtMoment =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Publishes_FabEventIngestedV1_with_every_envelope_field_mapped_to_wire_strings()
    {
        FakeEventBus bus = new();
        EventIngestedDomainEventHandler handler = new(
            bus, NullLogger<EventIngestedDomainEventHandler>.Instance);

        EventIdentifier identifier = EventIdentifier.New();
        EventIngestedDomainEvent domainEvent = new(
            identifier,
            FabIdentifier.From("munich"),
            Source.Plc,
            DeviceIdentifier.From("station-4"),
            Kind.From("PlcCycleStart"),
            OccurredAt.From(OccurredAtMoment),
            IngestedAt.From(IngestedAtMoment),
            Payload.From("{\"cycleId\":\"abc\"}"));

        await handler.Handle(domainEvent, CancellationToken.None);

        FabEventIngestedV1 v1 = bus.Published.OfType<FabEventIngestedV1>().ShouldHaveSingleItem();
        v1.EventIdentifier.ShouldBe(identifier.Value);
        v1.Fab.ShouldBe("munich");
        v1.Source.ShouldBe("plc");
        v1.Device.ShouldBe("station-4");
        v1.Kind.ShouldBe("PlcCycleStart");
        v1.OccurredAt.ShouldBe(OccurredAtMoment);
        v1.IngestedAt.ShouldBe(IngestedAtMoment);
        v1.Payload.ShouldBe("{\"cycleId\":\"abc\"}");
    }
}
