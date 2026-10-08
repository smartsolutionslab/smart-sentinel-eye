using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// T007 (spec 317, #2325) — FR-005/FR-006/FR-010. A declared discovery pair
/// holds an unregistered kind on both ingest insertion points, in a new file
/// so <c>IngestEventCommandHandlerTests</c> / <c>IngestEventBatchCommandHandlerTests</c>
/// stay characterisation-only, mirroring spec 269's
/// <c>IngestEventStrictModeTests</c> / <c>IngestEventBatchStrictModeTests</c>.
/// </summary>
[Collection(IngestVolumeCollection.Name)]
public class IngestEventQuarantineTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Berlin = FabIdentifier.From("berlin");

    private static EventEnvelope BuildEnvelope(
        EventIdentifier? identifier = null,
        DateTimeOffset? occurredAt = null,
        string kind = "NobodyDeclaredThis",
        Source? source = null) =>
        new(
            identifier ?? EventIdentifier.New(),
            Berlin,
            source ?? Source.Manual,
            DeviceIdentifier.From("station-4"),
            Kind.From(kind),
            OccurredAt.From(occurredAt ?? Now),
            Payload.From("{\"cycleId\":\"abc\"}"));

    private static InMemoryEventTypeAdmissionSource DiscoverySource(Source? source = null)
    {
        InMemoryEventTypeAdmissionSource admissionSource = new();
        admissionSource.DeclareDiscovery(Berlin, source ?? Source.Manual);
        return admissionSource;
    }

    private static IngestEventCommandHandler Handler(
        InMemoryEventRepository events, InMemoryDeadLetterRepository deadLetters, InMemoryEventTypeAdmissionSource source) =>
        new(events, deadLetters, new FakeClock(Now), new EventTypeAdmission(source),
            NullLogger<IngestEventCommandHandler>.Instance);

    private static IngestEventBatchCommandHandler BatchHandler(
        InMemoryEventRepository events, InMemoryDeadLetterRepository deadLetters, InMemoryEventTypeAdmissionSource source) =>
        new(events, deadLetters, new FakeClock(Now), new EventTypeAdmission(source),
            NullLogger<IngestEventBatchCommandHandler>.Instance);

    [Fact]
    public async Task A_held_envelope_writes_one_dead_letter_with_the_unknown_event_type_reason()
    {
        using RecordedIngestVolume recorded = new();
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        Result<EventIdentifier, IngestEventError> result = await Handler(events, deadLetters, DiscoverySource())
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<IngestEventError.EventTypeHeld>();
        DeadLetter held = deadLetters.DeadLetters.ShouldHaveSingleItem();
        held.Reason.ShouldBe(DeadLetterReason.UnknownEventType);
        held.Kind.ShouldBe(Kind.From("NobodyDeclaredThis"));
        held.Fab.ShouldBe(Berlin);
        held.Topic.Value.ShouldBe(DeliveryTopic.ForEnvelope(Berlin, Source.Manual, DeviceIdentifier.From("station-4")).Value);
        held.RawPayload.Value.ShouldBe(envelope.Payload.Value, "the held payload must be captured verbatim");
    }

    [Fact]
    public async Task A_held_envelope_is_not_stored_as_an_event()
    {
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        await Handler(events, deadLetters, DiscoverySource())
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        events.Events.ShouldBeEmpty("a held event must never produce an Event row (FR-010)");
    }

    [Fact]
    public async Task A_held_envelope_records_no_ingest_volume()
    {
        using RecordedIngestVolume recorded = new();
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        await Handler(events, deadLetters, DiscoverySource())
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        recorded.Measurements.ShouldBeEmpty("a hold is not an arrival, the same reasoning as a strict refusal");
    }

    /// <summary>FR-005 precedence: redelivery outranks the hold.</summary>
    [Fact]
    public async Task A_redelivery_is_still_a_redelivery_under_discovery()
    {
        InMemoryEventRepository events = new();
        EventIdentifier identifier = EventIdentifier.New();
        EventEnvelope envelope = BuildEnvelope(identifier, kind: "NobodyDeclaredThis");
        events.Add(EventAggregate.Ingest(
            envelope.Identifier, envelope.Fab, envelope.Source, envelope.Device,
            envelope.Kind, envelope.OccurredAt, envelope.Payload, new FakeClock(Now)));
        await events.SaveAsync(CancellationToken.None);
        InMemoryDeadLetterRepository deadLetters = new();

        Result<EventIdentifier, IngestEventError> result = await Handler(events, deadLetters, DiscoverySource())
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<IngestEventError.EventAlreadyIngested>();
        deadLetters.DeadLetters.ShouldBeEmpty("a redelivery must not be held");
    }

    /// <summary>FR-005 precedence: future skew outranks the hold.</summary>
    [Fact]
    public async Task Future_skew_outranks_a_hold()
    {
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        EventEnvelope envelope = BuildEnvelope(occurredAt: Now.AddMinutes(6), kind: "NobodyDeclaredThis");

        Result<EventIdentifier, IngestEventError> result = await Handler(events, deadLetters, DiscoverySource())
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.Error.ShouldBeOfType<IngestEventError.OccurredAtTooFarInFuture>();
        deadLetters.DeadLetters.ShouldBeEmpty("future skew must be reported as skew, not as a hold");
    }

    /// <summary>A registered kind under a declared discovery pair is unaffected.</summary>
    [Fact]
    public async Task A_registered_kind_under_discovery_is_admitted_not_held()
    {
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        InMemoryEventTypeAdmissionSource source = DiscoverySource();
        source.Register(Domain.RegisteredEventType.RegisteredEventType.Register(
            Berlin, Kind.From("PlcCycleStart"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now)));
        EventEnvelope envelope = BuildEnvelope(kind: "PlcCycleStart");

        Result<EventIdentifier, IngestEventError> result = await Handler(events, deadLetters, source)
            .HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        deadLetters.DeadLetters.ShouldBeEmpty();
    }

    // --- Batch handler ---

    [Fact]
    public async Task A_mixed_batch_stores_the_registered_envelope_and_holds_the_unknown_one_in_the_same_commit()
    {
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        InMemoryEventTypeAdmissionSource source = DiscoverySource();
        source.Register(Domain.RegisteredEventType.RegisteredEventType.Register(
            Berlin, Kind.From("PlcCycleStart"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now)));
        EventEnvelope registered = BuildEnvelope(kind: "PlcCycleStart");
        EventEnvelope unknown = BuildEnvelope(kind: "NobodyDeclaredThis");

        IngestEventBatchResult result = await BatchHandler(events, deadLetters, source)
            .HandleAsync(new IngestEventBatchCommand([registered, unknown]), CancellationToken.None);

        events.Events.ShouldHaveSingleItem().Id.ShouldBe(registered.Identifier);
        deadLetters.DeadLetters.ShouldHaveSingleItem().Kind.ShouldBe(Kind.From("NobodyDeclaredThis"));
        result.Refused.ShouldBeEmpty("a held envelope must not also be reported as refused");
    }

    [Fact]
    public async Task The_batch_consults_admission_once_for_a_declared_discovery_fab()
    {
        InMemoryEventRepository events = new();
        InMemoryDeadLetterRepository deadLetters = new();
        InMemoryEventTypeAdmissionSource source = DiscoverySource();
        EventEnvelope[] envelopes = [.. Enumerable.Range(0, 20).Select(i => BuildEnvelope(kind: $"Kind{i % 3}"))];

        await BatchHandler(events, deadLetters, source)
            .HandleAsync(new IngestEventBatchCommand(envelopes), CancellationToken.None);

        source.DeclaredSourceModesCalls.ShouldBe(1);
        source.RegisteredKindsCalls.ShouldBe(1, "one call for the one declared fab in this batch, not per envelope");
    }
}
