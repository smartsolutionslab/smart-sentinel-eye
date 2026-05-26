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
}
