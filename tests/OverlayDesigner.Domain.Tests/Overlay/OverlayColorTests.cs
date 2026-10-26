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
        var color = OverlayColor.From("#d32f2f");
        color.Value.ShouldBe("#D32F2FFF");
    }

    [Fact]
    public void From_canonicalises_an_eight_digit_value_to_upper_case()
    {
        var color = OverlayColor.From("#ff000080");
        color.Value.ShouldBe("#FF000080");
    }

    [Fact]
    public void From_preserves_an_already_canonical_value()
    {
        var color = OverlayColor.From("#D32F2FFF");
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
        var exception = Should.Throw<ArgumentException>(() => OverlayColor.From(raw));
        exception.ParamName.ShouldBe("value");
    }

    [Fact]
    public void From_rejects_null_naming_the_parameter()
    {
        var exception = Should.Throw<ArgumentException>(() => OverlayColor.From(null!));
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
        var a = OverlayColor.From("#d32f2f");
        var b = OverlayColor.From("#D32F2FFF");
        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    // Spec 463 (#3108), ADR-0190: a colour brighter than relative luminance 0.35 cannot be stored.
    // Alpha does not enter the luminance (spec D1).

    [Theory]
    [InlineData("#9F9F9F", "#9F9F9FFF")]
    [InlineData("#9F9F9FD9", "#9F9F9FD9")]
    [InlineData("#D32F2FFF", "#D32F2FFF")]
    public void From_accepts_a_colour_at_or_under_the_luminance_cap(string raw, string canonical)
    {
        OverlayColor.From(raw).Value.ShouldBe(canonical);
    }

    [Theory]
    [InlineData("#A0A0A0")]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFFFFD9")]
    [InlineData("#FFEB3BFF")]
    [InlineData("#00C853FF")]
    public void From_refuses_a_colour_brighter_than_the_luminance_cap(string raw)
    {
        var exception = Should.Throw<ArgumentException>(() => OverlayColor.From(raw));
        exception.ParamName.ShouldBe("value");
        exception.Message.ShouldContain("0.35");
    }

    [Theory]
    [InlineData("#FFFFFF10")]
    [InlineData("#FFFFFF00")]
    public void From_refuses_a_bright_colour_however_transparent_it_is(string raw)
    {
        Should.Throw<ArgumentException>(() => OverlayColor.From(raw));
    }

    [Fact]
    public void From_names_the_nearest_allowed_colour_when_it_refuses()
    {
        var exception = Should.Throw<ArgumentException>(() => OverlayColor.From("#FFFFFFD9"));
        exception.Message.ShouldContain("#9F9F9FD9");
    }

    [Fact]
    public void MaximumRelativeLuminance_is_the_pinned_cap()
    {
        // Reflection: the constant does not exist yet, so a direct reference would not compile.
        var field = typeof(OverlayColor).GetField("MaximumRelativeLuminance");
        field.ShouldNotBeNull("OverlayColor.MaximumRelativeLuminance must exist (plan C1)");
        field.GetRawConstantValue().ShouldBe(0.35);
    }

    [Fact]
    public void DefaultValue_is_the_nearest_allowed_colour_of_the_old_white_default()
    {
        OverlayColor.DefaultValue.ShouldBe("#9F9F9FD9");
    }

    [Fact]
    public void Default_is_under_the_cap_and_legible_as_text()
    {
        OverlayColor.Default.Value.ShouldBe("#9F9F9FD9");
        TextLegibility.IsLegible(OverlayColor.Default).ShouldBeTrue();
    }
}
