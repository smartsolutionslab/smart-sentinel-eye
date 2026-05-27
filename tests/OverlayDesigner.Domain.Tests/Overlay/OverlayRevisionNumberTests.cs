using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayRevisionNumberTests
{
    [Fact]
    public void One_is_the_canonical_starting_revision()
    {
        OverlayRevisionNumber.One.Value.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    public void From_accepts_any_positive_integer(int value)
    {
        OverlayRevisionNumber.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void From_rejects_zero_or_negative(int value)
    {
        Should.Throw<ArgumentException>(() => OverlayRevisionNumber.From(value));
    }

    [Fact]
    public void Next_returns_the_successor()
    {
        OverlayRevisionNumber.From(3).Next().Value.ShouldBe(4);
    }
}
