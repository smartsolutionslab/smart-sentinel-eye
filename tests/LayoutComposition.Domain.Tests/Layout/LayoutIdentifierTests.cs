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
}
