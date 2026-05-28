using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

public class IngestEventCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    private static EventEnvelope BuildEnvelope(
        EventIdentifier? identifier = null,
        DateTimeOffset? occurredAt = null) =>
        new(
            identifier ?? EventIdentifier.New(),
            FabIdentifier.From("munich"),
            Source.Plc,
            DeviceIdentifier.From("station-4"),
            Kind.From("PlcCycleStart"),
            OccurredAt.From(occurredAt ?? Now),
            Payload.From("{\"cycleId\":\"abc\"}"));

    [Fact]
    public async Task Happy_path_persists_the_event_and_raises_a_domain_event_carrying_the_envelope()
    {
        InMemoryEventRepository repo = new();
        IngestEventCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<IngestEventCommandHandler>.Instance);

        EventEnvelope envelope = BuildEnvelope();
        Result<EventIdentifier, IngestEventError> result =
            await handler.HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(envelope.Identifier);
        repo.Events.ShouldHaveSingleItem().Id.ShouldBe(envelope.Identifier);
    }

    [Fact]
    public async Task Duplicate_event_returns_EventAlreadyIngested_and_does_not_double_insert()
    {
        EventIdentifier identifier = EventIdentifier.New();
        InMemoryEventRepository repo = new();
        IngestEventCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<IngestEventCommandHandler>.Instance);

        Result<EventIdentifier, IngestEventError> first = await handler.HandleAsync(
            new IngestEventCommand(BuildEnvelope(identifier)), CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();

        Result<EventIdentifier, IngestEventError> second = await handler.HandleAsync(
            new IngestEventCommand(BuildEnvelope(identifier)), CancellationToken.None);

        second.IsSuccess.ShouldBeFalse();
        second.Error.ShouldBeOfType<IngestEventError.EventAlreadyIngested>();
        repo.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task OccurredAt_more_than_5_minutes_in_the_future_returns_typed_error()
    {
        InMemoryEventRepository repo = new();
        IngestEventCommandHandler handler = new(
            repo, new FakeClock(Now),
            NullLogger<IngestEventCommandHandler>.Instance);

        EventEnvelope envelope = BuildEnvelope(occurredAt: Now.AddMinutes(6));

        Result<EventIdentifier, IngestEventError> result =
            await handler.HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<IngestEventError.OccurredAtTooFarInFuture>();
        repo.Events.ShouldBeEmpty();
    }
}
