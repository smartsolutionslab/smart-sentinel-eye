using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.Event;

public class EventTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Ingest_assigns_every_envelope_field_and_stamps_IngestedAt_from_the_clock()
    {
        EventAggregate @event = new EventBuilder().WithClock(Now).Build();

        @event.Fab.Value.ShouldBe("munich");
        @event.Source.ShouldBe(Source.Plc);
        @event.Device.Value.ShouldBe("station-4");
        @event.Kind.Value.ShouldBe("PlcCycleStart");
        @event.IngestedAt.Value.ShouldBe(Now);
        @event.Payload.Value.ShouldBe("{\"cycleId\":\"abc\"}");
    }

    [Fact]
    public void Ingest_raises_an_EventIngestedDomainEvent_carrying_the_same_envelope()
    {
        EventAggregate @event = new EventBuilder().Build();
        EventIngestedDomainEvent raised = @event.PendingEvents
            .OfType<EventIngestedDomainEvent>()
            .ShouldHaveSingleItem();
        raised.Identifier.ShouldBe(@event.Id);
        raised.Source.ShouldBe(@event.Source);
        raised.Device.ShouldBe(@event.Device);
    }

    [Fact]
    public void Ingest_rejects_an_occurredAt_more_than_five_minutes_in_the_future()
    {
        DateTimeOffset future = Now.AddMinutes(6);
        Action act = () => new EventBuilder()
            .WithClock(Now)
            .WithOccurredAt(future)
            .Build();
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Ingest_accepts_an_occurredAt_within_the_five_minute_skew()
    {
        DateTimeOffset future = Now.AddMinutes(4);
        EventAggregate @event = new EventBuilder()
            .WithClock(Now)
            .WithOccurredAt(future)
            .Build();
        @event.OccurredAt.Value.ShouldBe(future);
    }
}
