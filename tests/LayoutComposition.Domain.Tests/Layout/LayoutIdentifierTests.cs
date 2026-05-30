using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_sortable_Guid_v7()
    {
        LayoutIdentifier a = LayoutIdentifier.New();
        LayoutIdentifier b = LayoutIdentifier.New();

        a.Value.ShouldNotBe(Guid.Empty);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => LayoutIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        Guid value = Guid.CreateVersion7();
        LayoutIdentifier id = LayoutIdentifier.From(value);
        id.Value.ShouldBe(value);
    }

    [Fact]
    public void Implicitly_unwraps_to_its_guid()
    {
        Guid guid = Guid.CreateVersion7();
        Guid unwrapped = LayoutIdentifier.From(guid);
        unwrapped.ShouldBe(guid);
    }

    [Fact]
    public void Comparison_operators_order_by_the_underlying_guid()
    {
        LayoutIdentifier earlier = LayoutIdentifier.From(new Guid("01900000-0000-7000-8000-000000000001"));
        LayoutIdentifier later = LayoutIdentifier.From(new Guid("01900000-0000-7000-8000-000000000002"));

        earlier.CompareTo(later).ShouldBeLessThan(0);
        (earlier < later).ShouldBeTrue();
        (earlier <= later).ShouldBeTrue();
        (later > earlier).ShouldBeTrue();
        (later >= earlier).ShouldBeTrue();
    }
}
