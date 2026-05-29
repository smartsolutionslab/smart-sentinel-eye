using System.Globalization;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Domain.Tests.Fakes;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class AuditEventTests
{
    private static readonly DateTimeOffset Occurred =
        DateTimeOffset.Parse("2026-05-29T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Received =
        DateTimeOffset.Parse("2026-05-29T08:14:33.090Z", CultureInfo.InvariantCulture);
    private static readonly Guid ActorGuid = Guid.CreateVersion7();
    private static readonly Guid EventGuid = Guid.CreateVersion7();
    private const string PayloadJson =
        """{"cameraIdentifier":"33333333-3333-3333-3333-333333333333","name":"north-gate"}""";

    private static V1Envelope SampleEnvelope() => new(
        EventTypeName: "CameraRegisteredV1",
        OccurredAt: Occurred,
        Fab: Option<FabIdentifier>.Some(FabIdentifier.From("munich")),
        Actor: ActorIdentifier.From(ActorGuid),
        ActorUsername: Option<string>.Some("admin@munich.test"),
        EventIdentifier: EventIdentifier.From(EventGuid),
        Payload: PayloadJson);

    private static V1Mapping SampleMapping() => new(
        Option<ResourceKind>.Some(ResourceKind.Camera),
        Option<ResourceIdentifier>.Some(
            ResourceIdentifier.From("33333333-3333-3333-3333-333333333333")));

    [Fact]
    public void From_stamps_a_fresh_audit_id_and_received_at_from_the_clock()
    {
        AuditEventEntity row = AuditEventEntity.From(SampleEnvelope(), SampleMapping(), new FakeClock(Received));

        row.Id.Value.ShouldNotBe(Guid.Empty);
        row.Id.Value.Version.ShouldBe(7);
        row.ReceivedAt.ShouldBe(Received);
    }

    [Fact]
    public void From_copies_every_envelope_field_onto_the_row()
    {
        AuditEventEntity row = AuditEventEntity.From(SampleEnvelope(), SampleMapping(), new FakeClock(Received));

        row.OccurredAt.ShouldBe(Occurred);
        row.EventKind.Value.ShouldBe("CameraRegisteredV1");
        row.Fab.Value.Value.ShouldBe("munich");
        row.Actor.Value.ShouldBe(ActorGuid);
        row.Actor.IsSystem.ShouldBeFalse();
        row.ActorUsername.Value.ShouldBe("admin@munich.test");
        row.EventIdentifier.Value.ShouldBe(EventGuid);
        row.Payload.ShouldBe(PayloadJson);
        row.PayloadSizeBytes.ShouldBe(
            System.Text.Encoding.UTF8.GetByteCount(PayloadJson));
        row.SchemaVersion.ShouldBe(AuditEventEntity.CurrentSchemaVersion);
    }

    [Fact]
    public void From_pipes_the_resource_mapping_through_to_the_row()
    {
        AuditEventEntity row = AuditEventEntity.From(SampleEnvelope(), SampleMapping(), new FakeClock(Received));

        row.ResourceKind.Value.ShouldBe(ResourceKind.Camera);
        row.ResourceIdentifier.Value.Value.ShouldBe("33333333-3333-3333-3333-333333333333");
    }

    [Fact]
    public void From_stores_an_unmapped_V1_with_null_resource_fields_but_still_records_the_event_kind()
    {
        AuditEventEntity row = AuditEventEntity.From(SampleEnvelope(), V1Mapping.Unmapped, new FakeClock(Received));

        row.ResourceKind.HasValue.ShouldBeFalse();
        row.ResourceIdentifier.HasValue.ShouldBeFalse();
        row.EventKind.Value.ShouldBe("CameraRegisteredV1");
    }

    [Fact]
    public void From_accepts_a_cross_fab_envelope_with_no_fab()
    {
        V1Envelope crossFab = SampleEnvelope() with { Fab = Option<FabIdentifier>.None };
        AuditEventEntity row = AuditEventEntity.From(crossFab, V1Mapping.Unmapped, new FakeClock(Received));

        row.Fab.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void From_records_a_system_actor_when_the_envelope_carries_one()
    {
        V1Envelope systemEvt = SampleEnvelope() with
        {
            Actor = ActorIdentifier.System,
            ActorUsername = Option<string>.None,
        };
        AuditEventEntity row = AuditEventEntity.From(systemEvt, V1Mapping.Unmapped, new FakeClock(Received));

        row.Actor.IsSystem.ShouldBeTrue();
        row.ActorUsername.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Payload_size_counts_utf8_bytes_not_chars()
    {
        // Multi-byte chars must contribute their full UTF-8 byte count.
        string payloadWithMultibyte = "{\"note\":\"Größenänderung\"}";
        V1Envelope envelope = SampleEnvelope() with { Payload = payloadWithMultibyte };
        AuditEventEntity row = AuditEventEntity.From(envelope, V1Mapping.Unmapped, new FakeClock(Received));

        row.PayloadSizeBytes.ShouldBe(
            System.Text.Encoding.UTF8.GetByteCount(payloadWithMultibyte));
        row.PayloadSizeBytes.ShouldBeGreaterThan(payloadWithMultibyte.Length);
    }
}
