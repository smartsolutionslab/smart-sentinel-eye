using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// Spec 020 FR-010. The batch handler exists to take the two round trips once
/// rather than once per event, and the ways that goes wrong are all about which
/// envelopes it decides not to insert.
/// </summary>
public class IngestEventBatchCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    private static EventEnvelope BuildEnvelope(
        EventIdentifier? identifier = null, DateTimeOffset? occurredAt = null) =>
        new(
            identifier ?? EventIdentifier.New(),
            FabIdentifier.From("munich"),
            Source.Plc,
            DeviceIdentifier.From("station-4"),
            Kind.From("PlcCycleStart"),
            OccurredAt.From(occurredAt ?? Now),
            Payload.From("{\"cycleId\":\"abc\"}"));

    private static IngestEventBatchCommandHandler Handler(InMemoryEventRepository repository) =>
        new(repository, new FakeClock(Now),
            NullLogger<IngestEventBatchCommandHandler>.Instance);

    [Fact]
    public async Task Stores_every_envelope_in_the_batch()
    {
        InMemoryEventRepository repository = new();

        IReadOnlyList<EventEnvelope> accepted = await Handler(repository).HandleAsync(
            new IngestEventBatchCommand([BuildEnvelope(), BuildEnvelope(), BuildEnvelope()]),
            CancellationToken.None);

        accepted.Count.ShouldBe(3);
        repository.Events.Count.ShouldBe(3);
    }

    /// <summary>
    /// FR-002/FR-003. Redelivery is the ordinary way an interruption ends now,
    /// so this is the common case rather than an edge one.
    /// </summary>
    [Fact]
    public async Task An_event_already_stored_is_not_inserted_again_but_is_still_acknowledged()
    {
        EventIdentifier identifier = EventIdentifier.New();
        InMemoryEventRepository repository = new();

        await Handler(repository).HandleAsync(
            new IngestEventBatchCommand([BuildEnvelope(identifier)]), CancellationToken.None);

        IReadOnlyList<EventEnvelope> accepted = await Handler(repository).HandleAsync(
            new IngestEventBatchCommand([BuildEnvelope(identifier)]), CancellationToken.None);

        repository.Events.Count.ShouldBe(1, "the redelivery was stored a second time");
        accepted.ShouldHaveSingleItem();
    }

    /// <summary>
    /// The duplicate that arrives <b>within one batch</b>, before either copy
    /// has been stored — so the database cannot answer for it and the existence
    /// query does not see it. Left alone it violates the unique constraint,
    /// fails the whole batch, and sends 199 healthy events down the slow path
    /// for a duplicate the idempotency rule was meant to absorb.
    /// </summary>
    [Fact]
    public async Task A_duplicate_inside_one_batch_is_inserted_once()
    {
        EventIdentifier identifier = EventIdentifier.New();
        InMemoryEventRepository repository = new();

        IReadOnlyList<EventEnvelope> accepted = await Handler(repository).HandleAsync(
            new IngestEventBatchCommand([BuildEnvelope(identifier), BuildEnvelope(identifier)]),
            CancellationToken.None);

        repository.Events.Count.ShouldBe(1);
        accepted.Count.ShouldBe(2, "both deliveries must be acknowledged; the event is stored");
    }

    /// <summary>
    /// A domain rule refuses this envelope and will refuse it identically for
    /// ever. It must not fail the batch, and it must still be acknowledged —
    /// the single-event path acknowledges these too, and if the batch path did
    /// not, the behaviour would depend on how many events happened to arrive
    /// together.
    /// </summary>
    [Fact]
    public async Task An_envelope_no_rule_will_ever_accept_is_left_out_but_does_not_fail_the_batch()
    {
        InMemoryEventRepository repository = new();
        EventEnvelope healthy = BuildEnvelope();
        EventEnvelope skewed = BuildEnvelope(occurredAt: Now.AddDays(30));

        IReadOnlyList<EventEnvelope> accepted = await Handler(repository).HandleAsync(
            new IngestEventBatchCommand([skewed, healthy]), CancellationToken.None);

        repository.Events.ShouldHaveSingleItem().Id.ShouldBe(healthy.Identifier);
        accepted.Count.ShouldBe(2);
    }
}
