using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class OverlayIdentifierTests
{
    [Fact]
    public void From_a_non_empty_Guid_wraps_the_value()
    {
        Guid raw = Guid.CreateVersion7();
        OverlayIdentifier identifier = OverlayIdentifier.From(raw);
        identifier.Value.ShouldBe(raw);
    }

    [Fact]
    public void From_Guid_Empty_throws_ArgumentException()
    {
        Action act = () => OverlayIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_the_underlying_Guid_string()
    {
        Guid raw = Guid.CreateVersion7();
        OverlayIdentifier identifier = OverlayIdentifier.From(raw);
        identifier.ToString().ShouldBe(raw.ToString());
    }

    [Fact]
    public void Two_identifiers_with_the_same_value_are_equal()
    {
        Guid raw = Guid.CreateVersion7();
        OverlayIdentifier a = OverlayIdentifier.From(raw);
        OverlayIdentifier b = OverlayIdentifier.From(raw);
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = OverlayIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        OverlayIdentifier earlier = OverlayIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        OverlayIdentifier later = OverlayIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
