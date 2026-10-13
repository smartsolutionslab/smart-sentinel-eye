using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Wall;

/// <summary>
/// Spec 258 US2: <c>CausingEventIdentifier</c> is a Guid-backed
/// <c>…Identifier</c> record struct (ADR-0039/0090), the same shape as
/// <c>WallIdentifier</c> (mirrors <c>WallIdentifierTests</c> /
/// <c>LayoutIdentifierTests</c>). Exists so <c>WallSceneChangedV1</c>'s
/// shape doesn't need a V2 later (plan.md §2), so it needs the same
/// coverage as any other identifier VO even though nothing exercised it
/// until now.
/// </summary>
public class CausingEventIdentifierTests
{
    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => CausingEventIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        var value = Guid.CreateVersion7();
        var id = CausingEventIdentifier.From(value);
        id.Value.ShouldBe(value);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        var guid = Guid.CreateVersion7();
        Guid unwrapped = CausingEventIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Two_identifiers_with_the_same_guid_are_equal()
    {
        var value = Guid.CreateVersion7();
        CausingEventIdentifier.From(value).ShouldBe(CausingEventIdentifier.From(value));
    }

    [Fact]
    public void Two_identifiers_with_different_guids_are_not_equal()
    {
        CausingEventIdentifier.From(Guid.CreateVersion7()).ShouldNotBe(CausingEventIdentifier.From(Guid.CreateVersion7()));
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        var earlier = CausingEventIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        var later = CausingEventIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
