using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

public class AuditingMessageHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T08:14:33Z", CultureInfo.InvariantCulture);

    private static V1Envelope Envelope(CameraRegisteredV1 evt, Guid? eventIdentifier = null) =>
        new(
            EventTypeName: nameof(CameraRegisteredV1),
            OccurredAt: evt.RegisteredAt,
            Fab: Option<FabIdentifier>.None,
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(eventIdentifier ?? Guid.CreateVersion7()),
            Payload: System.Text.Json.JsonSerializer.Serialize(evt));

    [Fact]
    public async Task Writes_a_row_for_a_mapped_V1()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7());

        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt), default);

        repo.Committed.Count.ShouldBe(1);
        repo.Committed[0].EventKind.Value.ShouldBe("CameraRegisteredV1");
        repo.Committed[0].ResourceKind.ShouldBe(ResourceKind.Camera);
        repo.Committed[0].ResourceIdentifier!.Value.ShouldBe(evt.Camera.ToString());
    }

    [Fact]
    public async Task Duplicate_event_identifier_is_absorbed_idempotently()
    {
        InMemoryAuditEventRepository repo = new();
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        CameraRegisteredV1 evt = new(
            Guid.CreateVersion7(), "north-gate", "rtsp://example/cam", Now, Guid.CreateVersion7());
        Guid eventId = Guid.CreateVersion7();

        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt, eventId), default);
        await handler.HandleAsync(typeof(CameraRegisteredV1), evt, Envelope(evt, eventId), default);

        repo.Committed.Count.ShouldBe(1);
        repo.SaveAsyncCallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Unmapped_V1_still_audits_with_null_resource_fields()
    {
        InMemoryAuditEventRepository repo = new();
        // A type that's deliberately NOT in V1ResourceMap (string
        // is a stand-in for a future V1 the registry has not yet
        // learned about).
        AuditingMessageHandler handler = new(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance);

        V1Envelope envelope = new(
            EventTypeName: "FutureV1",
            OccurredAt: Now,
            Fab: Option<FabIdentifier>.None,
            Actor: ActorIdentifier.System,
            ActorUsername: Option<string>.None,
            EventIdentifier: EventIdentifier.From(Guid.CreateVersion7()),
            Payload: "{}");

        await handler.HandleAsync(typeof(string), "irrelevant", envelope, default);

        repo.Committed.Count.ShouldBe(1);
        repo.Committed[0].ResourceKind.ShouldBeNull();
        repo.Committed[0].ResourceIdentifier.ShouldBeNull();
        repo.Committed[0].EventKind.Value.ShouldBe("FutureV1");
    }
}
