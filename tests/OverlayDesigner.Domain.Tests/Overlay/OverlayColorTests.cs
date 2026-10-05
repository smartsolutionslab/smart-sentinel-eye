using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 300 (#2349), ADR-0165, T005: an open colour value, canonical
/// <c>#RRGGBBAA</c> upper-case. <c>#RRGGBB</c> is accepted and canonicalised
/// to an opaque <c>FF</c> alpha. The pattern and default are pinned by the
/// frontend test (FR-020), so their literal values are asserted here too.
/// </summary>
public class OverlayColorTests
{
    [Fact]
    public void From_canonicalises_a_six_digit_value_to_opaque_eight_digit_upper_case()
    {
        OverlayColor color = OverlayColor.From("#d32f2f");
        color.Value.ShouldBe("#D32F2FFF");
    }

    [Fact]
    public void From_canonicalises_an_eight_digit_value_to_upper_case()
    {
        OverlayColor color = OverlayColor.From("#ff000080");
        color.Value.ShouldBe("#FF000080");
    }

    [Fact]
    public void From_preserves_an_already_canonical_value()
    {
        OverlayColor color = OverlayColor.From("#D32F2FFF");
        color.Value.ShouldBe("#D32F2FFF");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#F00")]
    [InlineData("#F00F")]
    [InlineData("#FF00")]
    [InlineData("#FF0000A")]
    [InlineData("#FF0000AAB")]
    [InlineData("#GG0000")]
    [InlineData("")]
    public void From_rejects_a_malformed_value_naming_the_parameter(string raw)
    {
        ArgumentException exception = Should.Throw<ArgumentException>(() => OverlayColor.From(raw));
        exception.ParamName.ShouldBe("value");
    }

    [Fact]
    public void From_rejects_null_naming_the_parameter()
    {
        ArgumentException exception = Should.Throw<ArgumentException>(() => OverlayColor.From(null!));
        exception.ParamName.ShouldBe("value");
    }

    [Fact]
    public void DefaultValue_is_the_pinned_white_at_eighty_five_percent_alpha()
    {
        OverlayColor.DefaultValue.ShouldBe("#FFFFFFD9");
    }

    [Fact]
    public void Default_is_From_of_the_pinned_default_value()
    {
        OverlayColor.Default.ShouldBe(OverlayColor.From(OverlayColor.DefaultValue));
        OverlayColor.Default.Value.ShouldBe("#FFFFFFD9");
    }

    [Fact]
    public void Pattern_is_the_pinned_eight_or_six_digit_hex_regex()
    {
        OverlayColor.Pattern.ShouldBe("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$");
    }

    [Fact]
    public void Two_colors_with_the_same_canonical_value_are_equal()
    {
        OverlayColor a = OverlayColor.From("#d32f2f");
        OverlayColor b = OverlayColor.From("#D32F2FFF");
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }
}
