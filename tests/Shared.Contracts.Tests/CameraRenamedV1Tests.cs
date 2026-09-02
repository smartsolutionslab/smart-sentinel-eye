using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.CameraCatalog;

namespace SmartSentinelEye.Shared.Contracts.Tests;

/// <summary>
/// Spec 033. Mirrors <see cref="CameraRegisteredV1Tests"/> — every V1 in this
/// project carries the same four checks, and the coverage gate (ADR-0065, 90%
/// here) is what keeps that true: adding a record without its test drops the
/// whole project below the gate rather than quietly leaving one type untested.
/// </summary>
public class CameraRenamedV1Tests
{
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-05-29T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    private static readonly DateTimeOffset RenamedAt =
        DateTimeOffset.Parse("2026-08-24T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Exposes_all_payload_fields_via_the_positional_constructor()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();

        CameraRenamedV1 evt = new(
            camera, "munich", "line-3-inlet", "line-4-inlet", RenamedAt, operatorId, Metadata: TestMetadata);

        evt.Camera.ShouldBe(camera);
        evt.Fab.ShouldBe("munich");
        evt.Name.ShouldBe("line-4-inlet");
        evt.RenamedAt.ShouldBe(RenamedAt);
        evt.RenamedBy.ShouldBe(operatorId);

        // The delta, and the reason this event carries two names. An audit entry
        // reading "renamed to line-4-inlet" records that something happened
        // without saying what was corrected.
        evt.PreviousName.ShouldBe("line-3-inlet");
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        CameraRenamedV1 evt = new(
            Guid.CreateVersion7(),
            "munich",
            "line-3-inlet",
            "line-4-inlet",
            RenamedAt,
            Guid.CreateVersion7(),
            Metadata: TestMetadata);

        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();

        CameraRenamedV1 first = new(
            camera, "munich", "line-3-inlet", "line-4-inlet", RenamedAt, operatorId, Metadata: TestMetadata);
        CameraRenamedV1 second = new(
            camera, "munich", "line-3-inlet", "line-4-inlet", RenamedAt, operatorId, Metadata: TestMetadata);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    /// <summary>
    /// A rename that differs only in letter case is a real change, and the two
    /// names normalise identically — so an event carrying them must not treat
    /// them as one. Records compare strings ordinally, which is what makes this
    /// hold here; the same distinction had to be made deliberately in the
    /// aggregate and in the EF value comparer.
    /// </summary>
    [Fact]
    public void A_case_only_rename_is_not_equal_to_no_rename_at_all()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();

        CameraRenamedV1 cased = new(
            camera, "munich", "Line-3-Inlet", "line-3-inlet", RenamedAt, operatorId, Metadata: TestMetadata);
        CameraRenamedV1 unchanged = new(
            camera, "munich", "line-3-inlet", "line-3-inlet", RenamedAt, operatorId, Metadata: TestMetadata);

        cased.ShouldNotBe(unchanged);
        cased.PreviousName.ShouldNotBe(cased.Name);
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        Guid camera = Guid.CreateVersion7();
        Guid operatorId = Guid.CreateVersion7();

        CameraRenamedV1 original = new(
            camera, "munich", "line-3-inlet", "line-4-inlet", RenamedAt, operatorId, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        CameraRenamedV1 deserialized = JsonSerializer.Deserialize<CameraRenamedV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
