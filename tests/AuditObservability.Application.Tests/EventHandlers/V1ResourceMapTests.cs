using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.Identity;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

public class V1ResourceMapTests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        null,
        null);

    private readonly V1ResourceMap _map = V1ResourceMap.Default;

    [Fact]
    public void Camera_V1_maps_to_camera_resource_kind()
    {
        Guid id = Guid.CreateVersion7();
        CameraRegisteredV1 evt = new(
            id, "north-gate", "rtsp://example/cam",
            DateTimeOffset.UtcNow, Guid.CreateVersion7(), Metadata: TestMetadata);

        V1Mapping mapping = _map.Lookup(typeof(CameraRegisteredV1), evt);

        mapping.Kind.HasValue.ShouldBeTrue();
        mapping.Kind.Value.ShouldBe(ResourceKind.Camera);
        mapping.ResourceIdentifier.HasValue.ShouldBeTrue();
        mapping.ResourceIdentifier.Value.Value.ShouldBe(id.ToString());
    }

    [Fact]
    public void Device_V1_picks_clientId_not_the_RegisteredClientIdentifier()
    {
        DeviceRegisteredV1 evt = new(
            Guid.CreateVersion7(), "plc-station-4", "plc", "station-4", "munich",
            DateTimeOffset.UtcNow, Metadata: TestMetadata);

        V1Mapping mapping = _map.Lookup(typeof(DeviceRegisteredV1), evt);

        mapping.Kind.Value.ShouldBe(ResourceKind.Device);
        mapping.ResourceIdentifier.Value.Value.ShouldBe("plc-station-4");
    }

    [Fact]
    public void Kiosk_V1_maps_to_kiosk_via_a_hand_tweak()
    {
        KioskEnrolledV1 evt = new(
            Guid.CreateVersion7(), "kiosk-pilot", "munich", DateTimeOffset.UtcNow, Metadata: TestMetadata);

        V1Mapping mapping = _map.Lookup(typeof(KioskEnrolledV1), evt);

        mapping.Kind.Value.ShouldBe(ResourceKind.Kiosk);
        mapping.ResourceIdentifier.Value.Value.ShouldBe("kiosk-pilot");
    }

    [Fact]
    public void AuditChunkArchivedV1_pivots_on_the_chunk_identifier()
    {
        Guid chunkId = Guid.CreateVersion7();
        AuditChunkArchivedV1 evt = new(
            chunkId, "munich", 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, "k", "m", Metadata: TestMetadata);

        V1Mapping mapping = _map.Lookup(typeof(AuditChunkArchivedV1), evt);

        mapping.Kind.HasValue.ShouldBeTrue();
        mapping.ResourceIdentifier.Value.Value.ShouldBe(chunkId.ToString());
    }

    [Fact]
    public void Lookup_of_a_non_IIntegrationEvent_type_returns_unmapped()
    {
        V1Mapping mapping = _map.Lookup(typeof(string), "anything");
        mapping.Kind.HasValue.ShouldBeFalse();
        mapping.ResourceIdentifier.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void MappedTypes_covers_a_meaningful_subset_of_Shared_Contracts_V1s()
    {
        // Sanity bound: there are several specs' worth of V1s on
        // develop; if the convention scanner ever silently broke
        // (e.g. namespace-tail dictionary lost an entry), this
        // floor catches it well before the strict architecture
        // test in spec 009 T070.
        _map.MappedTypes.Count.ShouldBeGreaterThanOrEqualTo(10);
        _map.MappedTypes.ShouldContain(typeof(CameraRegisteredV1));
        _map.MappedTypes.ShouldContain(typeof(AuditChunkArchivedV1));
    }
}
