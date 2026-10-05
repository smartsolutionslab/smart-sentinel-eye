using System.Globalization;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 300 (#2349), ADR-0165 §2, T006: the domain's half of the
/// compositor-independent worst-case text legibility rule. The shared
/// vectors (<see cref="TextLegibilityVectorTests"/>) are the exhaustive
/// cross-check against the independent reference generator; these are the
/// specific boundaries plan.md names by hand.
/// </summary>
public class TextLegibilityTests
{
    [Fact]
    public void MinimumContrast_is_the_pinned_WCAG_AA_threshold()
    {
        TextLegibility.MinimumContrast.ShouldBe(4.5);
    }

    [Fact]
    public void IsLegible_rejects_white_one_step_below_its_floor()
    {
        TextLegibility.IsLegible(OverlayColor.From("#FFFFFF74")).ShouldBeFalse();
    }

    [Fact]
    public void IsLegible_accepts_white_at_its_floor()
    {
        TextLegibility.IsLegible(OverlayColor.From("#FFFFFF75")).ShouldBeTrue();
    }

    [Fact]
    public void IsLegible_rejects_red_one_step_below_its_floor()
    {
        TextLegibility.IsLegible(OverlayColor.From("#D32F2FF8")).ShouldBeFalse();
    }

    [Fact]
    public void IsLegible_accepts_red_at_its_floor()
    {
        TextLegibility.IsLegible(OverlayColor.From("#D32F2FF9")).ShouldBeTrue();
    }

    [Fact]
    public void IsLegible_rejects_red_far_below_its_floor()
    {
        TextLegibility.IsLegible(OverlayColor.From("#D32F2F80")).ShouldBeFalse();
    }

    [Fact]
    public void IsLegible_accepts_fully_opaque_colours_that_meet_the_opaque_threshold()
    {
        TextLegibility.IsLegible(OverlayColor.From("#D32F2FFF")).ShouldBeTrue();
        TextLegibility.IsLegible(OverlayColor.From("#FFEB3BFF")).ShouldBeTrue();
    }

    [Fact]
    public void MinimumAlpha_of_white_is_the_global_floor_0x75()
    {
        TextLegibility.MinimumAlpha(OverlayColor.From("#FFFFFFFF")).ShouldBe((byte)0x75);
    }

    [Fact]
    public void MinimumAlpha_of_red_is_0xF9()
    {
        TextLegibility.MinimumAlpha(OverlayColor.From("#D32F2FFF")).ShouldBe((byte)0xF9);
    }

    [Fact]
    public void MinimumAlpha_is_always_legible_when_applied_back_to_the_same_colour()
    {
        OverlayColor atMinimum = OverlayColor.From(
            "#D32F2F" + TextLegibility.MinimumAlpha(OverlayColor.From("#D32F2FFF")).ToString("X2", CultureInfo.InvariantCulture));
        TextLegibility.IsLegible(atMinimum).ShouldBeTrue();
    }
}
