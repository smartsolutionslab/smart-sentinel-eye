using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutRevisionNumberTests
{
    [Fact]
    public void One_is_the_canonical_starting_revision()
    {
        LayoutRevisionNumber.One.Value.ShouldBe(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    public void From_accepts_any_positive_integer(int value)
    {
        LayoutRevisionNumber.From(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void From_rejects_zero_or_negative(int value)
    {
        Action act = () => LayoutRevisionNumber.From(value);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Next_returns_the_successor()
    {
        LayoutRevisionNumber.From(3).Next().Value.ShouldBe(4);
    }
}
