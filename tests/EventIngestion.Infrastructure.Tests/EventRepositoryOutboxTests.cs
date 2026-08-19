using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 021 T005 and T006. The order of two lines is the entire guarantee, and
/// it is invisible from every other test in this repository: the repository's
/// happy path behaves identically whether it announces before or after it
/// commits. These are the two cases that can tell the difference.
/// </summary>
public class EventRepositoryOutboxTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-19T09:00:00Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-001. A message captured after the commit is a message outside the
    /// transaction, which is the defect — so "both happened" is not the
    /// assertion, "in this order" is.
    /// </summary>
    [Fact]
    public async Task The_announcement_is_captured_before_the_write_is_committed()
    {
        List<string> order = [];
        RecordingDispatcher dispatcher = new(order);
        RecordingCommit commit = new(order);

        await using EventIngestionDbContext database = Database();
        database.Events.Add(AnEvent());

        EventRepository repository = new(database, commit, dispatcher);
        await repository.SaveAsync(CancellationToken.None);

        order.ShouldBe(["dispatch", "commit"], "the announcement must be inside the transaction");
    }

    /// <summary>
    /// FR-001, the half a naive fix gets wrong. If capturing the announcement
    /// fails, the write must not be committed — otherwise the row lands with no
    /// announcement, which is the defect this feature exists to close, reached
    /// by a different road.
    ///
    /// <para>
    /// The other half — that a rolled-back transaction discards announcements
    /// already captured — cannot honestly be shown here. It is Wolverine's
    /// behaviour, not this class's, and a fake asserting it would be testing the
    /// fake. That claim belongs to the integration case (T008), against a real
    /// database and a real outbox.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_capture_that_fails_commits_nothing()
    {
        List<string> order = [];
        RecordingCommit commit = new(order);

        await using EventIngestionDbContext database = Database();
        database.Events.Add(AnEvent());

        EventRepository repository = new(database, commit, new ThrowingDispatcher());

        await Should.ThrowAsync<AggregateException>(
            () => repository.SaveAsync(CancellationToken.None));

        order.ShouldBeEmpty("the write was committed even though its announcement was not captured");
    }

    /// <summary>
    /// Never connects. The repository reads the change tracker and delegates the
    /// commit to the outbox, so nothing here touches a database — which is what
    /// makes an ordering test possible without a provider the repo does not
    /// reference.
    /// </summary>
    private static EventIngestionDbContext Database() =>
        new(new DbContextOptionsBuilder<EventIngestionDbContext>()
            .UseNpgsql("Host=not-connected;Database=none;Username=none;Password=none")
            .Options);

    private static EventAggregate AnEvent() => EventAggregate.Ingest(
        EventIdentifier.New(),
        FabIdentifier.From("munich"),
        Source.Plc,
        DeviceIdentifier.From("station-4"),
        Kind.From("PlcCycleStart"),
        OccurredAt.From(Now),
        Payload.From("{\"cycleId\":\"abc\"}"),
        new FixedClock());

    private sealed class RecordingDispatcher(List<string> order) : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
        {
            order.Add("dispatch");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the broker is not reachable");
    }

    /// <summary>
    /// Stands in for the commit. Records only that it was asked for, and when —
    /// the durability behind it is Wolverine's and is verified against a real
    /// outbox in the integration case.
    /// </summary>
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
