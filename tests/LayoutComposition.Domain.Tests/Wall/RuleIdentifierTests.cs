using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 US2: <c>RuleIdentifier</c> is a Guid-backed <c>…Identifier</c>
/// record struct (ADR-0039/0090), the same shape as <c>WallIdentifier</c>
/// (mirrors <c>WallIdentifierTests</c> / <c>LayoutIdentifierTests</c>).
/// Exists so <c>WallSceneChangedV1</c>'s shape doesn't need a V2 later
/// (plan.md §2), so it needs the same coverage as any other identifier VO
/// even though nothing exercised it until now.
/// </summary>
public class RuleIdentifierTests
{
    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => RuleIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        Guid value = Guid.CreateVersion7();
        RuleIdentifier id = RuleIdentifier.From(value);
        id.Value.ShouldBe(value);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = RuleIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Two_identifiers_with_the_same_guid_are_equal()
    {
        Guid value = Guid.CreateVersion7();
        RuleIdentifier.From(value).ShouldBe(RuleIdentifier.From(value));
    }

    [Fact]
    public void Two_identifiers_with_different_guids_are_not_equal()
    {
        RuleIdentifier.From(Guid.CreateVersion7()).ShouldNotBe(RuleIdentifier.From(Guid.CreateVersion7()));
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        RuleIdentifier earlier = RuleIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        RuleIdentifier later = RuleIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
