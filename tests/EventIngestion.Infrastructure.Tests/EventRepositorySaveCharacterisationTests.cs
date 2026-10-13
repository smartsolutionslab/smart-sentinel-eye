using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.Event.Events;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Characterisation (spec 346, behaviour-preserving): pins what
/// <c>EventRepository.SaveAsync</c> offers to the dispatcher, in which order,
/// what it throws when an announcement fails and what it clears.
/// <see cref="EventRepositoryOutboxTests"/> pins dispatch-before-commit and
/// that a failed capture commits nothing; these add the per-event dispatch,
/// the offer-everything-then-throw rule, the exact aggregate message and the
/// cancellation carve-out. They must pass unmodified after the pending-event
/// drain and the failure aggregation are extracted.
/// </summary>
public class EventRepositorySaveCharacterisationTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-19T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Pending_events_are_dispatched_one_per_call_in_the_order_the_aggregates_were_added()
    {
        var first = AnEvent(1);
        var second = AnEvent(2);
        var third = AnEvent(3);
        List<string> order = [];
        var dispatcher = new ScriptedDispatcher(order);
        var commit = new RecordingCommit(order);

        await using var database = Database();
        database.Events.AddRange(first, second, third);

        await new EventRepository(database, commit, dispatcher).SaveAsync(CancellationToken.None);

        dispatcher.Offered.ShouldBe([first.Id, second.Id, third.Id]);
        dispatcher.CallSizes.ShouldBe([1, 1, 1]);
        order.ShouldBe(["dispatch", "dispatch", "dispatch", "commit"]);
    }

    [Fact]
    public async Task An_aggregate_with_no_pending_events_is_not_dispatched()
    {
        var announced = AnEvent(1);
        var alreadyDrained = AnEvent(2);
        alreadyDrained.ClearPendingEvents();
        List<string> order = [];
        var dispatcher = new ScriptedDispatcher(order);

        await using var database = Database();
        database.Events.AddRange(alreadyDrained, announced);

        await new EventRepository(database, new RecordingCommit(order), dispatcher)
            .SaveAsync(CancellationToken.None);

        dispatcher.Offered.ShouldBe([announced.Id]);
    }

    [Fact]
    public async Task Pending_events_are_cleared_from_the_aggregates_by_the_save()
    {
        var first = AnEvent(1);
        var second = AnEvent(2);
        List<string> order = [];

        await using var database = Database();
        database.Events.AddRange(first, second);

        await new EventRepository(database, new RecordingCommit(order), new ScriptedDispatcher(order))
            .SaveAsync(CancellationToken.None);

        first.PendingEvents.ShouldBeEmpty();
        second.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_dispatch_that_throws_still_offers_all_three_and_commits_nothing()
    {
        var first = AnEvent(1);
        var second = AnEvent(2);
        var third = AnEvent(3);
        List<string> order = [];
        var failure = new InvalidOperationException("handler two is broken");
        var dispatcher = new ScriptedDispatcher(order, onCall: call => call == 1 ? failure : null);
        var commit = new RecordingCommit(order);

        await using var database = Database();
        database.Events.AddRange(first, second, third);

        var thrown = await Should.ThrowAsync<AggregateException>(
            () => new EventRepository(database, commit, dispatcher).SaveAsync(CancellationToken.None));

        dispatcher.Offered.ShouldBe([first.Id, second.Id, third.Id]);
        thrown.Message.ShouldBe(
            "1 of 3 domain event(s) could not be captured; "
            + "this unit of work is not committed and must be discarded with its scope. "
            + "(handler two is broken)");
        thrown.InnerExceptions.Count.ShouldBe(1);
        thrown.InnerExceptions[0].ShouldBeSameAs(failure);
        order.ShouldNotContain("commit");
    }

    [Fact]
    public async Task Two_failing_dispatches_are_reported_together_in_the_order_they_failed()
    {
        var first = AnEvent(1);
        var second = AnEvent(2);
        var third = AnEvent(3);
        List<string> order = [];
        var failureOne = new InvalidOperationException("handler one is broken");
        var failureThree = new InvalidOperationException("handler three is broken");
        var dispatcher = new ScriptedDispatcher(
            order,
            onCall: call => call switch
            {
                0 => failureOne,
                2 => failureThree,
                _ => null,
            });

        await using var database = Database();
        database.Events.AddRange(first, second, third);

        var thrown = await Should.ThrowAsync<AggregateException>(
            () => new EventRepository(database, new RecordingCommit(order), dispatcher)
                .SaveAsync(CancellationToken.None));

        dispatcher.Offered.ShouldBe([first.Id, second.Id, third.Id]);
        thrown.Message.ShouldBe(
            "2 of 3 domain event(s) could not be captured; "
            + "this unit of work is not committed and must be discarded with its scope. "
            + "(handler one is broken) (handler three is broken)");
        thrown.InnerExceptions.Count.ShouldBe(2);
        thrown.InnerExceptions[0].ShouldBeSameAs(failureOne);
        thrown.InnerExceptions[1].ShouldBeSameAs(failureThree);
    }

    [Fact]
    public async Task A_cancellation_during_dispatch_propagates_as_itself_and_stops_the_save()
    {
        var first = AnEvent(1);
        var second = AnEvent(2);
        var third = AnEvent(3);
        List<string> order = [];
        var cancellation = new OperationCanceledException("shutting down");
        var dispatcher = new ScriptedDispatcher(order, onCall: call => call == 1 ? cancellation : null);

        await using var database = Database();
        database.Events.AddRange(first, second, third);

        var thrown = await Record.ExceptionAsync(
            () => new EventRepository(database, new RecordingCommit(order), dispatcher)
                .SaveAsync(CancellationToken.None));

        thrown.ShouldBeSameAs(cancellation);
        thrown.ShouldBeOfType<OperationCanceledException>();
        dispatcher.Offered.ShouldBe([first.Id, second.Id]);
        order.ShouldNotContain("commit");
    }

    private static EventIngestionDbContext Database() =>
        new(new DbContextOptionsBuilder<EventIngestionDbContext>()
            .UseNpgsql("Host=not-connected;Database=none;Username=none;Password=none")
            .Options);

    private static EventAggregate AnEvent(int number) => EventAggregate.Ingest(
        EventIdentifier.From(new Guid(0, 0, 0, [0, 0, 0, 0, 0, 0, 0, (byte)number])),
        FabIdentifier.From("munich"),
        Source.Plc,
        DeviceIdentifier.From("station-4"),
        Kind.From("PlcCycleStart"),
        OccurredAt.From(Now),
        Payload.From("{\"cycleId\":\"abc\"}"),
        new FixedClock());

    private sealed class ScriptedDispatcher(
        List<string> order,
        Func<int, Exception?>? onCall = null) : IDomainEventDispatcher
    {
        public List<EventIdentifier> Offered { get; } = [];

        public List<int> CallSizes { get; } = [];

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
        {
            var call = CallSizes.Count;
            var batch = domainEvents.ToArray();
            CallSizes.Add(batch.Length);
            Offered.AddRange(batch.Cast<EventIngestedDomainEvent>().Select(ingested => ingested.Identifier));
            order.Add("dispatch");

            var failure = onCall?.Invoke(call);
            return failure is null ? Task.CompletedTask : throw failure;
        }
    }

    private sealed class RecordingCommit(List<string> order) : ITransactionalCommit
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            order.Add("commit");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
