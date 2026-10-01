using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests.LayoutComposition;

/// <summary>
/// Spec 296, plan.md §1 — shape pin for <c>WallSceneSwitchRequestedV1</c>,
/// including the "<c>TargetLayout</c> set iff <c>Target == "Layout"</c>"
/// rule. Field order is load-bearing (<c>HandlerDeconstructionTests</c>).
/// </summary>
public class WallSceneSwitchRequestedV1Tests
{
    private static readonly DateTimeOffset Moment =
        DateTimeOffset.Parse("2026-09-30T08:14:33.040Z", CultureInfo.InvariantCulture);
    private static readonly EventMetadata TestMetadata = new(
        Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
        DateTimeOffset.Parse("2026-09-30T08:00:00Z", CultureInfo.InvariantCulture),
        null,
        null);

    [Fact]
    public void Exposes_every_field_via_the_positional_constructor()
    {
        Guid wall = Guid.CreateVersion7();
        Guid targetLayout = Guid.CreateVersion7();
        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        WallSceneSwitchRequestedV1 evt = new(
            wall, "Layout", targetLayout, rule, Moment, causingEvent, Metadata: TestMetadata);

        evt.Wall.ShouldBe(wall);
        evt.Target.ShouldBe("Layout");
        evt.TargetLayout.ShouldBe(targetLayout);
        evt.Rule.ShouldBe(rule);
        evt.RequestedAt.ShouldBe(Moment);
        evt.CausingEventIdentifier.ShouldBe(causingEvent);
        evt.Metadata.ShouldBe(TestMetadata);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        WallSceneSwitchRequestedV1 evt = new(
            Guid.CreateVersion7(), "Next", null, Guid.CreateVersion7(), Moment,
            Guid.CreateVersion7(), Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid wall = Guid.CreateVersion7();
        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        WallSceneSwitchRequestedV1 a = new(
            wall, "Next", null, rule, Moment, causingEvent, Metadata: TestMetadata);
        WallSceneSwitchRequestedV1 b = new(
            wall, "Next", null, rule, Moment, causingEvent, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        WallSceneSwitchRequestedV1 original = new(
            Guid.CreateVersion7(), "Layout", Guid.CreateVersion7(), Guid.CreateVersion7(), Moment,
            Guid.CreateVersion7(), Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        WallSceneSwitchRequestedV1 deserialized = JsonSerializer.Deserialize<WallSceneSwitchRequestedV1>(json)!;

        deserialized.ShouldBe(original);
    }

    /// <summary>
    /// Plan.md §1: pins the shape the record is constructed with — that
    /// <c>TargetLayout</c> round-trips when <c>Target == "Layout"</c>. It
    /// does NOT prove the record enforces the invariant itself (a plain
    /// record has no validation) — that is a domain/application concern,
    /// not this contract's.
    /// </summary>
    [Fact]
    public void When_Target_is_Layout_TargetLayout_is_set()
    {
        Guid targetLayout = Guid.CreateVersion7();
        WallSceneSwitchRequestedV1 evt = new(
            Guid.CreateVersion7(), "Layout", targetLayout, Guid.CreateVersion7(), Moment,
            Guid.CreateVersion7(), Metadata: TestMetadata);

        evt.TargetLayout.ShouldNotBeNull();
        evt.TargetLayout.ShouldBe(targetLayout);
    }

    [Fact]
    public void When_Target_is_Next_TargetLayout_is_null()
    {
        WallSceneSwitchRequestedV1 evt = new(
            Guid.CreateVersion7(), "Next", null, Guid.CreateVersion7(), Moment,
            Guid.CreateVersion7(), Metadata: TestMetadata);

        evt.TargetLayout.ShouldBeNull();
    }
}
