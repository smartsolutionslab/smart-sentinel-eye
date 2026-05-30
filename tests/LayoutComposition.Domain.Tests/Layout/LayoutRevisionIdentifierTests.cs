using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutRevisionIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_Guid_distinct_from_a_sibling()
    {
        LayoutRevisionIdentifier a = LayoutRevisionIdentifier.New();
        LayoutRevisionIdentifier b = LayoutRevisionIdentifier.New();

        a.Value.ShouldNotBe(Guid.Empty);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => LayoutRevisionIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = LayoutRevisionIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        LayoutRevisionIdentifier earlier = LayoutRevisionIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        LayoutRevisionIdentifier later = LayoutRevisionIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
