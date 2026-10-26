using System.Globalization;
using System.Reflection;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Domain.Tests.Overlay;

/// <summary>
/// Spec 463 (#3108), ADR-0190, spec D3: <c>OverlayColor.NearestAllowed(string)</c> keeps chromaticity and
/// alpha and lowers luminance to the cap. The method does not exist yet, so it is reached by reflection:
/// the tests fail for the missing behaviour rather than failing to compile.
/// </summary>
public class OverlayColorNearestAllowedTests
{
    private static string NearestAllowed(string canonicalHex)
    {
        var method = typeof(OverlayColor).GetMethod(
            "NearestAllowed",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string)]);
        method.ShouldNotBeNull("OverlayColor.NearestAllowed(string) must exist (plan C1)");
        return (string)method.Invoke(null, [canonicalHex])!;
    }

    [Theory]
    [InlineData("#FFFFFFD9", "#9F9F9FD9")]
    [InlineData("#FFEB3BFF", "#AFA125FF")]
    [InlineData("#FFFFFF80", "#9F9F9F80")]
    [InlineData("#00C853FF", "#00B84BFF")]
    [InlineData("#D32F2FFF", "#D32F2FFF")]
    public void NearestAllowed_maps_the_pinned_vectors(string stored, string expected)
    {
        NearestAllowed(stored).ShouldBe(expected);
    }

    [Fact]
    public void NearestAllowed_keeps_the_alpha_and_leaves_text_alpha_raising_to_the_migrator()
    {
        NearestAllowed("#FFFF0099").ShouldBe("#A5A50099");
    }

    [Fact]
    public void NearestAllowed_returns_a_compliant_colour_unchanged()
    {
        NearestAllowed("#9F9F9FD9").ShouldBe("#9F9F9FD9");
        NearestAllowed("#D32F2FFF").ShouldBe("#D32F2FFF");
    }

    [Fact]
    public void NearestAllowed_yields_a_colour_From_accepts_for_every_grid_point_and_keeps_alpha()
    {
        int[] steps = [0, 51, 102, 153, 204, 255];
        foreach (var red in steps)
        {
            foreach (var green in steps)
            {
                foreach (var blue in steps)
                {
                    var stored = string.Create(CultureInfo.InvariantCulture, $"#{red:X2}{green:X2}{blue:X2}D9");
                    var nearest = NearestAllowed(stored);

                    Should.NotThrow(() => OverlayColor.From(nearest), $"{stored} -> {nearest}");
                    nearest.Substring(7).ShouldBe("D9");
                    NearestAllowed(nearest).ShouldBe(nearest, $"idempotent for {stored}");
                }
            }
        }
    }
}
