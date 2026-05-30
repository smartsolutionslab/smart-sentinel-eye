using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.EventIngestion;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// The generic bus subscriber: each concrete <c>Handle</c> entry point
/// delegates to the shared audit path, which reads the
/// <see cref="EventMetadata"/> envelope and writes one row.
/// </summary>
public class IntegrationEventAuditHandlerTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-30T08:14:33Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-05-30T08:00:00Z", CultureInfo.InvariantCulture);

    private static IntegrationEventAuditHandler Build(InMemoryAuditEventRepository repo) =>
        new(new AuditingMessageHandler(
            repo, V1ResourceMap.Default, new FakeClock(Now),
            NullLogger<AuditingMessageHandler>.Instance));

    [Fact]
    public async Task Handle_maps_metadata_actor_and_resource_pivot_for_a_camera_event()
    {
        InMemoryAuditEventRepository repo = new();
        Guid actor = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid cameraId = Guid.CreateVersion7();
        CameraRegisteredV1 evt = new(
            cameraId, "north-gate", "rtsp://example/cam", OccurredAt, actor,
            Metadata: new EventMetadata(eventId, OccurredAt, Fab: null, Actor: actor));

        await Build(repo).Handle(evt, default);

        repo.Committed.Count.ShouldBe(1);
        var row = repo.Committed[0];
        row.EventKind.Value.ShouldBe("CameraRegisteredV1");
        row.OccurredAt.ShouldBe(OccurredAt);
        row.EventIdentifier.Value.ShouldBe(eventId);
        row.Actor.Value.ShouldBe(actor);
        row.Fab.ShouldBeNull();
        row.ResourceKind.ShouldBe(ResourceKind.Camera);
        row.ResourceIdentifier!.Value.ShouldBe(cameraId.ToString());
        row.Payload.ShouldContain("north-gate");
    }

    [Fact]
    public async Task Handle_uses_envelope_fab_and_falls_back_to_System_actor_when_absent()
    {
        InMemoryAuditEventRepository repo = new();
        FabEventIngestedV1 evt = new(
            Guid.CreateVersion7(), "munich", "plc", "press-7", "alarm",
            OccurredAt, OccurredAt, "{}",
            Metadata: new EventMetadata(Guid.CreateVersion7(), OccurredAt, Fab: "munich", Actor: null));

        await Build(repo).Handle(evt, default);

        repo.Committed.Count.ShouldBe(1);
        var row = repo.Committed[0];
        row.Fab!.Value.ShouldBe("munich");
        row.Actor.IsSystem.ShouldBeTrue();
    }
}
