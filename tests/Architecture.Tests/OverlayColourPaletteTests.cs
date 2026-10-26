using System.Text.RegularExpressions;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 463 (#3108), ADR-0190 ("every token in the palette"), plan Phase 4a: every colour primitive in
/// <c>tokens.css</c> (ADR-0148) is either a colour <see cref="OverlayColor.From"/> accepts at full alpha, or
/// is named in <see cref="ExcludedPrimitives"/> with its reason. Adding a primitive, or moving one across the
/// cap, fails here until the list is updated on purpose.
/// </summary>
public class OverlayColourPaletteTests
{
    private const string TokenFile = "apps/shared/src/ui/tokens/tokens.css";

    private const string OverCap = "brighter than relative luminance 0.35 (ADR-0190)";

    private const string StatusHue = "status vocabulary reserved by ADR-0146 item 4";

    private static readonly Regex PrimitiveDeclaration = new(
        @"^\s*--(?<name>(?!(?:color|space|text|font|tracking|radius|shadow|duration|ease|z)-)[a-z]+(?:-\d+)?)\s*:\s*(?<value>oklch\([^;]+\)|#[0-9a-fA-F]{6})\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, string> ExcludedPrimitives = BuildExcluded();

    [Fact]
    public void Every_token_primitive_is_accepted_by_OverlayColor_or_excluded_with_a_reason()
    {
        List<string> unaccounted = [];
        foreach (var (name, hex) in Primitives())
        {
            if (ExcludedPrimitives.ContainsKey(name))
            {
                continue;
            }

            if (!Accepts(hex))
            {
                unaccounted.Add($"{name} ({hex})");
            }
        }

        unaccounted.ShouldBeEmpty(
            "these primitives are refused by OverlayColor.From but are not named in ExcludedPrimitives: "
            + string.Join(", ", unaccounted));
    }

    [Fact]
    public void Every_primitive_excluded_as_over_the_cap_is_refused_by_OverlayColor()
    {
        var primitives = Primitives();
        List<string> accepted = [];
        foreach (var (name, reason) in ExcludedPrimitives.Where(entry => entry.Value == OverCap))
        {
            primitives.ContainsKey(name).ShouldBeTrue($"{name} is excluded ({reason}) but tokens.css declares no such primitive.");
            if (Accepts(primitives[name]))
            {
                accepted.Add($"{name} ({primitives[name]})");
            }
        }

        accepted.ShouldBeEmpty(
            "these primitives are excluded as over the cap yet OverlayColor.From accepts them: "
            + string.Join(", ", accepted));
    }

    [Fact]
    public void Every_excluded_primitive_exists_in_the_token_file()
    {
        var primitives = Primitives();

        ExcludedPrimitives.Keys.Where(name => !primitives.ContainsKey(name)).ShouldBeEmpty();
    }

    [Fact]
    public void The_pinned_swatch_primitives_are_all_accepted()
    {
        var primitives = Primitives();
        string[] swatches =
        [
            "gray-500", "gray-600", "gray-700", "gray-800", "gray-850", "gray-900", "gray-950",
            "cyan-600", "cyan-700", "cyan-800", "cyan-900", "cyan-950", "black",
        ];

        foreach (var name in swatches)
        {
            primitives.ContainsKey(name).ShouldBeTrue(name);
            Accepts(primitives[name]).ShouldBeTrue($"{name} ({primitives[name]}) must be under the cap");
        }
    }

    private static Dictionary<string, string> BuildExcluded()
    {
        Dictionary<string, string> excluded = new(StringComparer.Ordinal);
        foreach (var step in new[] { 50, 100, 200, 300, 400 })
        {
            excluded[$"gray-{step}"] = OverCap;
        }

        foreach (var step in new[] { 50, 100, 200, 300, 400, 500 })
        {
            excluded[$"cyan-{step}"] = OverCap;
        }

        excluded["white"] = OverCap;
        foreach (var hue in new[] { "green", "red", "amber" })
        {
            foreach (var step in new[] { 50, 500, 700, 900 })
            {
                excluded[$"{hue}-{step}"] = StatusHue;
            }
        }

        return excluded;
    }

    private static bool Accepts(string hex)
    {
        try
        {
            OverlayColor.From(hex + "FF");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> Primitives()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, TokenFile.Replace('/', Path.DirectorySeparatorChar)));
        Dictionary<string, string> primitives = new(StringComparer.Ordinal);
        foreach (Match match in PrimitiveDeclaration.Matches(css))
        {
            var name = match.Groups["name"].Value;
            var (r, g, b) = OklchColorResolver.ParseColorLiteral(match.Groups["value"].Value.Trim(), name);
            primitives.TryAdd(name, $"#{r:X2}{g:X2}{b:X2}");
        }

        primitives.Count.ShouldBeGreaterThan(30, "the primitive scan found too few declarations in tokens.css");
        return primitives;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
