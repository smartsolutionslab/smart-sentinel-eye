using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayRevisionIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_Guid_distinct_from_a_sibling()
    {
        OverlayRevisionIdentifier a = OverlayRevisionIdentifier.New();
        OverlayRevisionIdentifier b = OverlayRevisionIdentifier.New();
        a.Value.ShouldNotBe(Guid.Empty);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Should.Throw<ArgumentException>(() => OverlayRevisionIdentifier.From(Guid.Empty));
    }
}
