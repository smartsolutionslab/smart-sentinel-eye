using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Commands;

/// <summary>
/// Phase 4a (spec 269 T003d). Strict-mode cases for
/// <see cref="IngestEventBatchCommandHandler"/>, in a new file so
/// <c>IngestEventBatchCommandHandlerTests</c> stays characterisation-only.
/// Every case here is red on arrival except
/// <see cref="Future_skew_outranks_an_unregistered_kind"/> — see
/// <c>IngestEventStrictModeTests</c>'s remarks for why.
/// </summary>
[Collection(IngestVolumeCollection.Name)]
public class IngestEventBatchStrictModeTests
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

    private static IngestEventBatchCommandHandler Handler(
        InMemoryEventRepository repository, InMemoryEventTypeAdmissionSource source) =>
        new(repository, new InMemoryDeadLetterRepository(), new FakeClock(Now), new EventTypeAdmission(source),
            NullLogger<IngestEventBatchCommandHandler>.Instance);

    /// <summary>Undocumented-green — see this class's remarks.</summary>
    [Fact]
    public async Task Future_skew_outranks_an_unregistered_kind()
    {
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        EventEnvelope skewed = BuildEnvelope(occurredAt: Now.AddDays(30));

        IngestEventBatchResult result =
            await Handler(repo, source).HandleAsync(new IngestEventBatchCommand([skewed]), CancellationToken.None);

        RefusedEnvelope refused = result.Refused.ShouldHaveSingleItem();
        refused.Reason.ShouldBeOfType<IngestEventError.OccurredAtTooFarInFuture>();
    }

    [Fact]
    public async Task The_batch_refuses_only_the_unregistered_envelopes()
    {
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        source.Register(RegisteredEventType.Register(
            Dresden, Kind.From("PlcCycleStart"), OperatorIdentifier.From(Guid.CreateVersion7()), new FakeClock(Now)));
        EventEnvelope registered = BuildEnvelope(kind: "PlcCycleStart");
        EventEnvelope unregistered = BuildEnvelope(kind: "NobodyDeclaredThis");

        IngestEventBatchResult result = await Handler(repo, source).HandleAsync(
            new IngestEventBatchCommand([registered, unregistered]), CancellationToken.None);

        repo.Events.ShouldHaveSingleItem().Id.ShouldBe(registered.Identifier);
        RefusedEnvelope refused = result.Refused.ShouldHaveSingleItem();
        refused.Envelope.Identifier.ShouldBe(unregistered.Identifier);
        refused.Reason.ShouldBeOfType<IngestEventError.EventTypeNotRegistered>();
    }

    [Fact]
    public async Task The_batch_consults_admission_once()
    {
        InMemoryEventRepository repo = new();
        InMemoryEventTypeAdmissionSource source = new();
        source.DeclareStrict(Dresden, Source.Manual);
        EventEnvelope[] envelopes = [.. Enumerable.Range(0, 20).Select(i => BuildEnvelope(kind: $"Kind{i % 3}"))];

        await Handler(repo, source).HandleAsync(new IngestEventBatchCommand(envelopes), CancellationToken.None);

        source.DeclaredSourceModesCalls.ShouldBe(1);
        source.RegisteredKindsCalls.ShouldBe(1, "one call for the one strict fab in this batch, not per envelope");
    }
}
