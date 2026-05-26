using SmartSentinelEye.LayoutComposition.Domain.Layout;

namespace SmartSentinelEye.LayoutComposition.Domain.Tests.Layout;

public class LayoutNameTests
{
    [Fact]
    public void From_accepts_a_normal_name()
    {
        LayoutName name = LayoutName.From("Line-1 Entrance");
        name.Value.ShouldBe("Line-1 Entrance");
    }

    [Fact]
    public void From_trims_leading_and_trailing_whitespace()
    {
        LayoutName name = LayoutName.From("  Line-1  ");
        name.Value.ShouldBe("Line-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_blank_input(string raw)
    {
        Action act = () => LayoutName.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_input_above_the_maximum_length()
    {
        string tooLong = new('a', LayoutName.MaximumLength + 1);
        Action act = () => LayoutName.From(tooLong);
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public void From_rejects_input_containing_a_line_break(string raw)
    {
        Should.Throw<ArgumentException>(() => LayoutName.From(raw));
    }
}
