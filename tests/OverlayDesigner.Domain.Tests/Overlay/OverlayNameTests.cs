using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class OverlayNameTests
{
    [Fact]
    public void From_accepts_a_normal_name()
    {
        OverlayName name = OverlayName.From("Line-1 Title");
        name.Value.ShouldBe("Line-1 Title");
    }

    [Fact]
    public void From_trims_leading_and_trailing_whitespace()
    {
        OverlayName name = OverlayName.From("  Line-1  ");
        name.Value.ShouldBe("Line-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_blank_input(string raw)
    {
        Action act = () => OverlayName.From(raw);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void From_rejects_input_above_the_maximum_length()
    {
        string tooLong = new('a', OverlayName.MaximumLength + 1);
        Action act = () => OverlayName.From(tooLong);
        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public void From_rejects_input_containing_a_line_break(string raw)
    {
        Should.Throw<ArgumentException>(() => OverlayName.From(raw));
    }
}
