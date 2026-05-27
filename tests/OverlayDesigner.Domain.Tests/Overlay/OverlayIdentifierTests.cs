using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayIdentifierTests
{
    [Fact]
    public void New_returns_a_non_empty_sortable_Guid_v7()
    {
        OverlayIdentifier a = OverlayIdentifier.New();
        OverlayIdentifier b = OverlayIdentifier.New();
        a.Value.ShouldNotBe(Guid.Empty);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void From_rejects_an_empty_Guid()
    {
        Action act = () => OverlayIdentifier.From(Guid.Empty);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_round_trips_a_non_empty_Guid()
    {
        Guid value = Guid.CreateVersion7();
        OverlayIdentifier id = OverlayIdentifier.From(value);
        id.Value.ShouldBe(value);
    }
}
