using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.Shared.Contracts.Tests;

/// <summary>
/// Spec 403 (#3008): the trailing optional <c>PreviousVersion</c> orders a
/// camera's address changes by the version each was made against. Additive per
/// ADR-0073, so an old reader ignores it and an old message reads as null.
/// </summary>
public class CameraAddressChangedV1Tests
{
    private static readonly Guid CameraGuid = Guid.Parse("0197a1b2-0000-7000-8000-0000000000d2", CultureInfo.InvariantCulture);

    private static readonly Guid OperatorGuid = Guid.Parse("0197a1b2-0000-7000-8000-000000000001", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset ChangedMoment =
        DateTimeOffset.Parse("2026-10-14T09:00:00Z", CultureInfo.InvariantCulture);

    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-10-14T08:00:00Z", CultureInfo.InvariantCulture),
        "munich",
        null);

    private sealed record PreSpec403CameraAddressChangedV1(
        Guid Camera,
        string Fab,
        string PreviousUrl,
        string Url,
        DateTimeOffset ChangedAt,
        Guid ChangedBy,
        EventMetadata Metadata);

    private static CameraAddressChangedV1 Event(int? previousVersion = null) =>
        new(
            CameraGuid,
            "munich",
            "rtsp://10.0.5.12/h264",
            "rtsp://10.0.5.44/h264",
            ChangedMoment,
            OperatorGuid,
            TestMetadata,
            previousVersion);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        var evt = Event(previousVersion: 5);

        evt.Camera.ShouldBe(CameraGuid);
        evt.Fab.ShouldBe("munich");
        evt.PreviousUrl.ShouldBe("rtsp://10.0.5.12/h264");
        evt.Url.ShouldBe("rtsp://10.0.5.44/h264");
        evt.ChangedAt.ShouldBe(ChangedMoment);
        evt.ChangedBy.ShouldBe(OperatorGuid);
        evt.Metadata.ShouldBe(TestMetadata);
        evt.PreviousVersion.ShouldBe(5);
    }

    [Fact]
    public void An_omitted_previous_version_is_null()
    {
        Event().PreviousVersion.ShouldBeNull();
    }

    [Fact]
    public void JSON_round_trip_preserves_the_previous_version()
    {
        var original = Event(previousVersion: 7);

        var deserialized = JsonSerializer.Deserialize<CameraAddressChangedV1>(JsonSerializer.Serialize(original))!;

        deserialized.ShouldBe(original);
        deserialized.PreviousVersion.ShouldBe(7);
    }

    [Fact]
    public void JSON_without_the_member_deserializes_with_a_null_version_and_every_other_field_intact()
    {
        var json = JsonSerializer.SerializeToNode(Event(previousVersion: 7))!.AsObject();
        json.Remove("PreviousVersion");

        var deserialized = JsonSerializer.Deserialize<CameraAddressChangedV1>(json.ToJsonString())!;

        deserialized.PreviousVersion.ShouldBeNull();
        deserialized.ShouldBe(Event());
    }

    [Fact]
    public void JSON_with_the_member_deserializes_into_the_seven_member_reader_of_before_spec_403()
    {
        var json = JsonSerializer.Serialize(Event(previousVersion: 7));

        var old = JsonSerializer.Deserialize<PreSpec403CameraAddressChangedV1>(json)!;

        old.ShouldBe(new PreSpec403CameraAddressChangedV1(
            CameraGuid,
            "munich",
            "rtsp://10.0.5.12/h264",
            "rtsp://10.0.5.44/h264",
            ChangedMoment,
            OperatorGuid,
            TestMetadata));
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal_and_a_different_version_is_not()
    {
        Event(previousVersion: 3).ShouldBe(Event(previousVersion: 3));
        Event(previousVersion: 3).GetHashCode().ShouldBe(Event(previousVersion: 3).GetHashCode());
        Event(previousVersion: 3).ShouldNotBe(Event(previousVersion: 4));
        Event(previousVersion: 3).ShouldNotBe(Event());
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        Event(previousVersion: 1).ShouldBeAssignableTo<IIntegrationEvent>();
    }
}
