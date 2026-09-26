using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests.LayoutComposition;

/// <summary>
/// Spec 258 US1, plan.md §1 — shape pin for <c>WallConfiguredV1</c>.
/// Field order is load-bearing (<c>HandlerDeconstructionTests</c>).
/// </summary>
public class WallConfiguredV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-09-26T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-09-26T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid wall = Guid.CreateVersion7();
        Guid sceneA = Guid.CreateVersion7();
        Guid sceneB = Guid.CreateVersion7();
        WallConfiguredV1 evt = new(wall, "Line 3 rotation", [sceneA, sceneB], sceneA, Moment, Metadata: TestMetadata);

        evt.Wall.ShouldBe(wall);
        evt.Name.ShouldBe("Line 3 rotation");
        evt.Scenes.ShouldBe([sceneA, sceneB]);
        evt.Showing.ShouldBe(sceneA);
        evt.ConfiguredAt.ShouldBe(Moment);
        evt.Metadata.ShouldBe(TestMetadata);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        Guid sceneA = Guid.CreateVersion7();
        WallConfiguredV1 evt = new(
            Guid.CreateVersion7(), "Line 3 rotation", [sceneA], sceneA, Moment, Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid wall = Guid.CreateVersion7();
        Guid sceneA = Guid.CreateVersion7();
        Guid sceneB = Guid.CreateVersion7();
        WallConfiguredV1 a = new(wall, "Line 3 rotation", [sceneA, sceneB], sceneA, Moment, Metadata: TestMetadata);
        WallConfiguredV1 b = new(wall, "Line 3 rotation", [sceneA, sceneB], sceneA, Moment, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        Guid sceneA = Guid.CreateVersion7();
        Guid sceneB = Guid.CreateVersion7();
        WallConfiguredV1 original = new(
            Guid.CreateVersion7(), "Line 3 rotation", [sceneA, sceneB], sceneA, Moment, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        WallConfiguredV1 deserialized = JsonSerializer.Deserialize<WallConfiguredV1>(json)!;

        deserialized.ShouldBe(original);
    }
}
