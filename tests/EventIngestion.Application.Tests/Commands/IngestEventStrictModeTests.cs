using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// Phase 4a (spec 269 T003d). Strict-mode cases for
/// <see cref="IngestEventCommandHandler"/>, in a new file so
/// <c>IngestEventCommandHandlerTests</c> stays characterisation-only (spec
/// 269 tasks.md T002 — that file's only permitted edit is the constructor
/// line). Two cases are undocumented-green rather than red: both
/// <see cref="A_redelivery_is_still_a_redelivery_under_strict"/> and
/// <see cref="Future_skew_outranks_an_unregistered_kind"/> are decided by a
/// check that runs and returns before <c>EventTypeAdmission</c> is ever
/// consulted (the existence check, and the skew catch — plan.md §6.2), so
/// both pass from the T002 prelude onward and do not wait on T005's real
/// strict/discovery logic. See the phase 4a report for the full, verified
/// list.
/// </summary>
[Collection(IngestVolumeCollection.Name)]
public class IngestEventStrictModeTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33.040Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static EventEnvelope BuildEnvelope(
        EventIdentifier? identifier = null, DateTimeOffset? occurredAt = null, string kind = "NobodyDeclaredThis") =>
        new(
            identifier ?? EventIdentifier.New(),
            Dresden,
            Source.Manual,
            DeviceIdentifier.From("station-4"),
            Kind.From(kind),
            OccurredAt.From(occurredAt ?? Now),
            Payload.From("{\"cycleId\":\"abc\"}"));

    private static IngestEventCommandHandler Handler(
        InMemoryEventRepository repository, InMemoryEventTypeAdmissionSource source) =>
        new(repository, new FakeClock(Now), new EventTypeAdmission(source),
            NullLogger<IngestEventCommandHandler>.Instance);

    [Fact]
    public async Task A_strict_source_refuses_an_unregistered_kind()
    {
        using RecordedIngestVolume recorded = new();
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        EventEnvelope envelope = BuildEnvelope();

        Result<EventIdentifier, IngestEventError> result =
            await Handler(repo, source).HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<IngestEventError.EventTypeNotRegistered>();
        repo.Events.ShouldBeEmpty("a refused envelope must not be stored");
        recorded.Measurements.ShouldBeEmpty("a refusal is not an arrival");
    }

    /// <summary>
    /// An event stored while the source was discovery must still answer
    /// <c>EventAlreadyIngested</c> once the source has since gone strict and
    /// its kind is unregistered (spec.md's precedence scenario, FR-007).
    /// Undocumented-green — see this class's remarks.
    /// </summary>
    [Fact]
    public async Task A_redelivery_is_still_a_redelivery_under_strict()
    {
        InMemoryEventRepository repo = new();
        EventIdentifier identifier = EventIdentifier.New();
        EventEnvelope envelope = BuildEnvelope(identifier);
        repo.Add(EventAggregate.Ingest(
            envelope.Identifier, envelope.Fab, envelope.Source, envelope.Device,
            envelope.Kind, envelope.OccurredAt, envelope.Payload, new FakeClock(Now)));
        await repo.SaveAsync(CancellationToken.None);

        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual); // now strict; the kind is still unregistered

        Result<EventIdentifier, IngestEventError> result =
            await Handler(repo, source).HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<IngestEventError.EventAlreadyIngested>();
    }

    /// <summary>
    /// Undocumented-green — see this class's remarks.
    /// </summary>
    [Fact]
    public async Task Future_skew_outranks_an_unregistered_kind()
    {
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        EventEnvelope envelope = BuildEnvelope(occurredAt: Now.AddMinutes(6));

        Result<EventIdentifier, IngestEventError> result =
            await Handler(repo, source).HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.Error.ShouldBeOfType<IngestEventError.OccurredAtTooFarInFuture>();
    }

    [Fact]
    public async Task The_refusal_names_fab_source_and_kind()
    {
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        EventEnvelope envelope = BuildEnvelope(kind: "NobodyDeclaredThis");

        Result<EventIdentifier, IngestEventError> result =
            await Handler(repo, source).HandleAsync(new IngestEventCommand(envelope), CancellationToken.None);

        result.Error.Message.ShouldContain("dresden");
        result.Error.Message.ShouldContain("manual");
        result.Error.Message.ShouldContain("NobodyDeclaredThis");
    }
}
