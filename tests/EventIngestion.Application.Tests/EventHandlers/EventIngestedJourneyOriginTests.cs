using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.EventHandlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.EventIngestion.Application.Tests.EventHandlers;

/// <summary>
/// Spec 026. Ingestion publishes from a background service draining a channel,
/// where nothing is in progress — so without a journey of its own the event is
/// published as an orphan and no downstream work can be traced back to it.
/// </summary>
public class EventIngestedJourneyOriginTests
{
    private static readonly DateTimeOffset OccurredAtMoment =
        DateTimeOffset.Parse("2026-08-22T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset IngestedAtMoment =
        DateTimeOffset.Parse("2026-08-22T08:14:33.040Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-001, and the only ordering that matters. A journey begun *around* the
    /// publish is what the message inherits; one begun and closed beforehand
    /// leaves the publish exactly as orphaned as it is today, while a call
    /// count would report both as done.
    /// </summary>
    [Fact]
    public async Task The_publish_happens_inside_the_journey_it_begins()
    {
        RecordingJourneyOrigin journeys = new();
        OpenJourneyRecordingBus bus = new(journeys);

        await HandlerFor(bus, journeys).Handle(DomainEvent(), CancellationToken.None);

        bus.OpenAtPublish.ShouldBe(1, "the publish must be caused by the journey, not merely preceded by one");
    }

    /// <summary>
    /// FR-006 / SC-005. Ingestion stores up to 200 deliveries in one batch and
    /// the dispatcher invokes handlers one domain event at a time, so per-event
    /// falls out of the structure — this asserts the structure rather than
    /// trusting it.
    ///
    /// <para>
    /// A batch-level journey is less code, still produces a joined trace, and
    /// still reads as correct from the effect end. It also merges two hundred
    /// unrelated plant-floor events, which is what makes "what did this event
    /// cause" unanswerable.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_event_in_a_batch_begins_its_own_journey()
    {
        RecordingJourneyOrigin journeys = new();
        FakeEventBus bus = new();
        EventIngestedDomainEventHandler handler = HandlerFor(bus, journeys);

        await handler.Handle(DomainEvent(), CancellationToken.None);
        await handler.Handle(DomainEvent(), CancellationToken.None);
        await handler.Handle(DomainEvent(), CancellationToken.None);

        journeys.Begun.Count.ShouldBe(3, "three events are three journeys");
    }

    /// <summary>
    /// Left open, the next event published on the same thread would be recorded
    /// as caused by this one — a fabricated relationship, which is worse than a
    /// missing one because it reads as an answer.
    /// </summary>
    [Fact]
    public async Task A_journey_does_not_outlive_the_event_that_began_it()
    {
        RecordingJourneyOrigin journeys = new();

        await HandlerFor(new FakeEventBus(), journeys).Handle(DomainEvent(), CancellationToken.None);

        journeys.Open.ShouldBe(0);
    }

    private static EventIngestedDomainEventHandler HandlerFor(IEventBus bus, IJourneyOrigin journeys) =>
        new(bus, journeys, NullLogger<EventIngestedDomainEventHandler>.Instance);

    private static EventIngestedDomainEvent DomainEvent() => new(
        EventIdentifier.New(),
        FabIdentifier.From("munich"),
        Source.Plc,
        DeviceIdentifier.From("station-4"),
        Kind.From("PlcCycleStart"),
        OccurredAt.From(OccurredAtMoment),
        IngestedAt.From(IngestedAtMoment),
        Payload.From("{\"cycleId\":\"abc\"}"));

    /// <summary>
    /// Notes how many journeys were open at the moment of the publish, which is
    /// the question a call count cannot answer.
    /// </summary>
    private sealed class OpenJourneyRecordingBus(RecordingJourneyOrigin journeys) : IEventBus
    {
        public int OpenAtPublish { get; private set; }

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            OpenAtPublish = journeys.Open;
            return Task.CompletedTask;
        }
    }
}
