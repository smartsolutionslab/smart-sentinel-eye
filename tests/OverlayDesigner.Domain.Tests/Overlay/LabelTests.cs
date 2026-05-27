using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

public class LabelTests
{
    [Fact]
    public void From_accepts_a_normal_label_payload()
    {
        Label label = Label.From("Production Line 1", 0.5m, 0.05m, 0.3m, 0.08m, 48);

        label.Text.ShouldBe("Production Line 1");
        label.NormalizedX.ShouldBe(0.5m);
        label.NormalizedY.ShouldBe(0.05m);
        label.NormalizedWidth.ShouldBe(0.3m);
        label.NormalizedHeight.ShouldBe(0.08m);
        label.FontSizePx.ShouldBe(48);
    }

    [Fact]
    public void From_trims_text_whitespace()
    {
        Label label = Label.From("  Padded  ", 0m, 0m, 0.1m, 0.1m, 16);
        label.Text.ShouldBe("Padded");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_blank_text(string raw)
    {
        Should.Throw<ArgumentException>(() => Label.From(raw, 0m, 0m, 0.1m, 0.1m, 16));
    }

    [Fact]
    public void From_rejects_text_above_the_maximum_length()
    {
        string tooLong = new('a', Label.MaximumTextLength + 1);
        Should.Throw<ArgumentException>(() => Label.From(tooLong, 0m, 0m, 0.1m, 0.1m, 16));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedX_outside_0_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => Label.From("t", (decimal)value, 0m, 0.1m, 0.1m, 16));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedY_outside_0_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => Label.From("t", 0m, (decimal)value, 0.1m, 0.1m, 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedWidth_outside_0_exclusive_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => Label.From("t", 0m, 0m, (decimal)value, 0.1m, 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void From_rejects_normalizedHeight_outside_0_exclusive_to_1(double value)
    {
        Should.Throw<ArgumentException>(() => Label.From("t", 0m, 0m, 0.1m, (decimal)value, 16));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(257)]
    public void From_rejects_fontSizePx_outside_the_range(int value)
    {
        Should.Throw<ArgumentException>(() => Label.From("t", 0m, 0m, 0.1m, 0.1m, value));
    }

    [Fact]
    public void Two_labels_with_the_same_payload_are_equal()
    {
        Label a = Label.From("text", 0.2m, 0.3m, 0.4m, 0.5m, 24);
        Label b = Label.From("text", 0.2m, 0.3m, 0.4m, 0.5m, 24);
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
