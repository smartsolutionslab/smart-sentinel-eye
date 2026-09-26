using System.Globalization;
using System.Text.Json;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;

namespace SmartSentinelEye.Shared.Contracts.Tests.LayoutComposition;

/// <summary>
/// Spec 258 US1, plan.md §1 — shape pin for <c>WallSceneChangedV1</c>,
/// including the "<c>Rule</c>/<c>CausingEventIdentifier</c> set iff
/// <c>Cause == "Rule"</c>" rule plan.md's "Why <c>Cause</c> is a string
/// plus two nullable fields" note calls for. Field order is load-bearing
/// (<c>HandlerDeconstructionTests</c>).
/// </summary>
public class WallSceneChangedV1Tests
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
        Guid previous = Guid.CreateVersion7();
        Guid current = Guid.CreateVersion7();
        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        WallSceneChangedV1 evt = new(
            wall, previous, current, 3L, "Rule", rule, causingEvent, Moment, Metadata: TestMetadata);

        evt.Wall.ShouldBe(wall);
        evt.PreviousLayout.ShouldBe(previous);
        evt.CurrentLayout.ShouldBe(current);
        evt.SceneVersion.ShouldBe(3L);
        evt.Cause.ShouldBe("Rule");
        evt.Rule.ShouldBe(rule);
        evt.CausingEventIdentifier.ShouldBe(causingEvent);
        evt.ChangedAt.ShouldBe(Moment);
        evt.Metadata.ShouldBe(TestMetadata);
    }

    [Fact]
    public void Implements_IIntegrationEvent_so_Wolverine_can_route_it()
    {
        WallSceneChangedV1 evt = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1L,
            "Operator", null, null, Moment, Metadata: TestMetadata);
        evt.ShouldBeAssignableTo<IIntegrationEvent>();
    }

    [Fact]
    public void Records_with_the_same_payload_are_equal()
    {
        Guid wall = Guid.CreateVersion7();
        Guid previous = Guid.CreateVersion7();
        Guid current = Guid.CreateVersion7();
        WallSceneChangedV1 a = new(
            wall, previous, current, 1L, "Operator", null, null, Moment, Metadata: TestMetadata);
        WallSceneChangedV1 b = new(
            wall, previous, current, 1L, "Operator", null, null, Moment, Metadata: TestMetadata);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void JSON_round_trip_preserves_every_field()
    {
        WallSceneChangedV1 original = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 7L,
            "Rule", Guid.CreateVersion7(), Guid.CreateVersion7(), Moment, Metadata: TestMetadata);

        string json = JsonSerializer.Serialize(original);
        WallSceneChangedV1 deserialized = JsonSerializer.Deserialize<WallSceneChangedV1>(json)!;

        deserialized.ShouldBe(original);
    }

    /// <summary>
    /// Plan.md §1: "A Shared.Contracts test pins the rule that the pair is
    /// set iff <c>Cause == "Rule"</c>." This asserts the shape the record is
    /// constructed with round-trips; it does NOT prove the record enforces
    /// the invariant itself (a plain record has no validation) — that is a
    /// domain/application concern, not this contract's.
    /// </summary>
    [Fact]
    public void When_Cause_is_Rule_both_Rule_and_CausingEventIdentifier_are_set()
    {
        Guid rule = Guid.CreateVersion7();
        Guid causingEvent = Guid.CreateVersion7();
        WallSceneChangedV1 evt = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2L,
            "Rule", rule, causingEvent, Moment, Metadata: TestMetadata);

        evt.Rule.ShouldNotBeNull();
        evt.CausingEventIdentifier.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Reconfigured")]
    public void When_Cause_is_not_Rule_Rule_and_CausingEventIdentifier_are_null(string cause)
    {
        WallSceneChangedV1 evt = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2L,
            cause, null, null, Moment, Metadata: TestMetadata);

        evt.Rule.ShouldBeNull();
        evt.CausingEventIdentifier.ShouldBeNull();
    }
}
