using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 300 (#2349), ADR-0165, T006: renamed from <c>LabelTests</c>. These
/// guards moved from <c>Label.From</c> onto <see cref="TextContent.From"/>
/// verbatim when <c>Label</c> became <see cref="OverlayElement"/> with a
/// nullable <see cref="TextContent"/> component (T004-T006).
/// </summary>
public class TextContentTests
{
    [Fact]
    public void From_accepts_a_normal_payload()
    {
        TextContent content = TextContent.From("Production Line 1", 48);

        content.Value.ShouldBe("Production Line 1");
        content.FontSizePx.ShouldBe(48);
    }

    [Fact]
    public void From_trims_text_whitespace()
    {
        TextContent content = TextContent.From("  Padded  ", 16);
        content.Value.ShouldBe("Padded");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_rejects_blank_text(string raw)
    {
        Should.Throw<ArgumentException>(() => TextContent.From(raw, 16));
    }

    [Fact]
    public void From_rejects_text_above_the_maximum_length()
    {
        string tooLong = new('a', TextContent.MaximumTextLength + 1);
        Should.Throw<ArgumentException>(() => TextContent.From(tooLong, 16));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(257)]
    public void From_rejects_fontSizePx_outside_the_range(int value)
    {
        Should.Throw<ArgumentException>(() => TextContent.From("t", value));
    }

    [Fact]
    public void Two_contents_with_the_same_payload_are_equal()
    {
        TextContent a = TextContent.From("text", 24);
        TextContent b = TextContent.From("text", 24);
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
