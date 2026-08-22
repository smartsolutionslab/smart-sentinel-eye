using System.Globalization;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.StreamDistribution.Application.EventHandlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Domain.Stream.Events;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.EventHandlers;

/// <summary>
/// Spec 027. The health watcher publishes from a background loop, where nothing
/// is in progress — so without a journey the announcement is an orphan and
/// audit-observability's record of it cannot be traced back to the check that
/// made it.
/// </summary>
public class StreamHealthChangedJourneyOriginTests
{
    private static readonly DateTimeOffset ChangedAtMoment =
        DateTimeOffset.Parse("2026-08-22T09:31:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-001, and the only ordering that matters. A journey begun *around* the
    /// publish is what the announcement inherits; one begun and closed
    /// beforehand leaves it exactly as orphaned as it is today, while a call
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
    /// FR-003. A sweep touches every camera in the fab, so one journey per sweep
    /// would merge every camera that changed onto one origin — which still reads
    /// as correct from the downstream end and makes "what did this observation
    /// cause" unanswerable.
    ///
    /// <para>
    /// It holds here because the dispatcher invokes handlers one domain event at
    /// a time, and the handler sits behind that rather than in the loop. This
    /// asserts the structure rather than establishing it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_camera_that_changed_begins_its_own_journey()
    {
        RecordingJourneyOrigin journeys = new();
        StreamHealthChangedDomainEventHandler handler = HandlerFor(new FakeBus(), journeys);

        await handler.Handle(DomainEvent(), CancellationToken.None);
        await handler.Handle(DomainEvent(), CancellationToken.None);
        await handler.Handle(DomainEvent(), CancellationToken.None);

        journeys.Begun.Count.ShouldBe(3, "three changes are three journeys");
    }

    /// <summary>
    /// Left open, the next announcement on the same thread would be recorded as
    /// caused by this one — a fabricated relationship, which is worse than a
    /// missing one because it reads as an answer.
    /// </summary>
    [Fact]
    public async Task A_journey_does_not_outlive_the_change_that_began_it()
    {
        RecordingJourneyOrigin journeys = new();

        await HandlerFor(new FakeBus(), journeys).Handle(DomainEvent(), CancellationToken.None);

        journeys.Open.ShouldBe(0);
    }

    /// <summary>
    /// FR-004, SC-004. A journey that failed to begin otherwise looks identical
    /// to one that began and caused nothing: same name, no children, no error.
    /// This defect was shipped in spec 026 and caught in code review.
    /// </summary>
    [Fact]
    public async Task A_failed_publish_marks_the_journey_rather_than_ending_it_quietly()
    {
        RecordingJourneyOrigin journeys = new();
        InvalidOperationException refused = new("the outbox refused the insert");

        await Should.ThrowAsync<InvalidOperationException>(
            HandlerFor(new ThrowingBus(refused), journeys).Handle(DomainEvent(), CancellationToken.None));

        journeys.Failure.ShouldBeSameAs(refused);
        journeys.Open.ShouldBe(0, "a failed journey still ends");
    }

    /// <summary>
    /// SC-004's other half. A status that is always set carries no information,
    /// so the case above only means something alongside this one.
    /// </summary>
    [Fact]
    public async Task A_successful_publish_leaves_the_journey_unmarked()
    {
        RecordingJourneyOrigin journeys = new();

        await HandlerFor(new FakeBus(), journeys).Handle(DomainEvent(), CancellationToken.None);

        journeys.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task The_announcement_still_carries_every_field()
    {
        FakeBus bus = new();

        await HandlerFor(bus, new RecordingJourneyOrigin()).Handle(DomainEvent(), CancellationToken.None);

        StreamHealthChangedV1 announced = bus.Published.OfType<StreamHealthChangedV1>().ShouldHaveSingleItem();
        announced.FromState.ShouldBe(StreamState.Healthy.Value);
        announced.ToState.ShouldBe(StreamState.Degraded.Value);
        announced.ChangedAt.ShouldBe(ChangedAtMoment);
        announced.Error.ShouldBe("probe timed out");
    }

    private static StreamHealthChangedDomainEventHandler HandlerFor(IEventBus bus, IJourneyOrigin journeys) =>
        new(bus, journeys);

    private static StreamHealthChangedDomainEvent DomainEvent() => new(
        Stream: StreamIdentifier.From(Guid.CreateVersion7()),
        Camera: CameraIdentifier.From(Guid.CreateVersion7()),
        FromState: StreamState.Healthy,
        ToState: StreamState.Degraded,
        ChangedAt: ChangedAtMoment,
        Error: "probe timed out");

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

    /// <summary>Refuses every publish, the way a rejected outbox insert does.</summary>
    private sealed class ThrowingBus(Exception refusal) : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull => Task.FromException(refusal);
    }
}
