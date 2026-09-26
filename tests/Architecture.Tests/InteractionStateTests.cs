using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards spec 268 (issue #2336), ADR-0146 discipline item 5: "Real interaction
/// states. Rest, hover, pressed, focus-visible, disabled and loading, designed
/// per variant. A uniform opacity fade is not a state." Phase-4a colour is
/// **red** (spec §6) — every fact below is expected to fail on unmodified
/// `develop`, naming exactly what plan.md §5.1 lists.
///
/// <para>
/// Scans <c>apps/*/src/**/*.{ts,tsx}</c>, tests excluded, through
/// <see cref="TypeScriptSource"/> — the same string-literal-only reader
/// <c>SharedUiTokenUsageTests</c> uses (lifted, spec 268 T001), so a rule aimed
/// at a Tailwind utility never trips on a class name that only looks like one
/// inside an identifier or a comment.
/// </para>
/// </summary>
public class InteractionStateTests
{
    // WCAG 1.4.3 (Contrast (Minimum)) — normal text against its background.
    private const double MinimumTextContrast = 4.5;

    // WCAG 1.4.11 (Non-text Contrast) — UI component boundaries and focus
    // indicators. Also this spec's own choice (plan.md §2.3) for a disabled
    // label's contrast against its surface: 1.4.3 exempts inactive components,
    // so 3:1 is not mandated by the criterion, but is picked here so "disabled"
    // still reads rather than vanishing.
    private const double MinimumNonTextContrast = 3.0;

    private static readonly Regex OpacityBehindStateVariant = new(
        @"\b(?:hover|active|focus|focus-visible|focus-within|disabled|enabled|aria-disabled|aria-busy"
        + @"|group-hover|peer-disabled):opacity-\d+",
        RegexOptions.Compiled);

    private static readonly Regex TriadFocusIndicator = new(
        @"\b(?:ring|outline|ring-offset)-accent-(?:active|fault|warning)\b",
        RegexOptions.Compiled);

    private static readonly Regex BoxShadowFocusRing = new(
        @"(?:focus-visible|focus|:focus-visible\]):ring-\d",
        RegexOptions.Compiled);

    private static readonly Regex FocusOutlinePrefix = new(
        @"(?<prefix>focus-visible:|has-\[:focus-visible\]:)outline-\S+",
        RegexOptions.Compiled);

    private static readonly Regex TriadAffordanceFill = new(
        @"\bbg-accent-(?:active|warning)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Fact 1 (plan.md §5.1). Red on develop: Button.tsx (×3 — the base
    /// `disabled:opacity-50` plus `hover:opacity-90` on `primary` and `danger`),
    /// Input.tsx, ChainRecoveryNotice.tsx and WallForm.tsx (×2 — the Up/Down
    /// scene buttons, spec 258).
    /// </summary>
    [Fact]
    public void No_opacity_utility_behind_an_interaction_state_variant()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            string content = TypeScriptSource.StringLiteralContent(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in OpacityBehindStateVariant.Matches(content))
            {
                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "an interaction-state variant applies an opacity utility — ADR-0146 item 5: \"a uniform opacity "
            + "fade is not a state\". Use the state's own designed fill/colour class instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 2 (plan.md §5.1). Red on develop: Input.tsx, DataTable.tsx and
    /// GridDesigner.tsx all draw their focus ring in `ring-accent-active`, the
    /// triad's "go" colour — ADR-0146 reserves the triad for affordance, not
    /// selection state, spec 257's handover.
    /// </summary>
    [Fact]
    public void No_triad_colour_draws_a_focus_indicator()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            string content = TypeScriptSource.StringLiteralContent(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in TriadFocusIndicator.Matches(content))
            {
                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a focus indicator cites a triad colour — use --color-focus-ring instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 3 (plan.md §5.1). Red on develop: Button.tsx, Input.tsx,
    /// DataTable.tsx and GridDesigner.tsx all draw focus with `ring-2` (a
    /// `box-shadow`), which forced-colors mode drops.
    /// </summary>
    [Fact]
    public void No_box_shadow_draws_a_focus_ring()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            string content = TypeScriptSource.StringLiteralContent(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in BoxShadowFocusRing.Matches(content))
            {
                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a focus indicator is drawn with a box-shadow ring-* utility, which forced-colors mode drops — use "
            + "an outline-* utility instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 4 (plan.md §5.1). For every `focus-visible:`/`has-[:focus-visible]:`
    /// prefix that draws an outline anywhere in a file, that same file must also
    /// carry the full three-utility recipe under the same prefix: `outline-2`,
    /// `outline-offset-2` and `outline-focus-ring`. Red on develop: Button.tsx,
    /// Input.tsx, DataTable.tsx and GridDesigner.tsx each only ever pair the
    /// prefix with `outline-none`.
    /// </summary>
    [Fact]
    public void Every_focus_outline_carries_the_full_recipe()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            string content = TypeScriptSource.StringLiteralContent(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            // TypeScriptSource joins each source string literal on its own line;
            // re-joining with a space reconstructs one class "soup" per file, so
            // a recipe split across concatenated literals (Button.tsx's `base`,
            // built from three `'...' + '...' + '...'` segments) is still seen
            // as one utility list, exactly as it renders at runtime.
            string classSoup = content.Replace('\n', ' ');

            HashSet<string> prefixes = [.. FocusOutlinePrefix.Matches(classSoup).Select(match => match.Groups["prefix"].Value)];

            foreach (string prefix in prefixes)
            {
                string[] required = [$"{prefix}outline-2", $"{prefix}outline-offset-2", $"{prefix}outline-focus-ring"];
                string[] missing = [.. required.Where(utility => !classSoup.Contains(utility, StringComparison.Ordinal))];

                if (missing.Length > 0)
                {
                    violations.Add($"{relativePath} ({prefix}): missing {string.Join(", ", missing)}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a focus-visible outline is missing part of its recipe — every focus outline is "
            + "`outline-2 outline-offset-2 outline-focus-ring` under the same prefix:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 5 (plan.md §5.1). Red on develop: `Button.tsx`'s `primary` variant is
    /// `bg-accent-active`, the triad's green — ADR-0146 reserves the triad for
    /// affordance/selection, and `primary` must read as the cyan accent instead.
    /// </summary>
    [Fact]
    public void Primitives_use_the_accent_not_the_triad_for_affordance()
    {
        DirectoryInfo root = RepositorySource.Root();
        string primitivesRoot = Path.Combine(root.FullName, "apps/shared/src/ui/primitives");
        List<string> violations = [];

        foreach (string relativePath in ScannedFilesUnder(root, primitivesRoot))
        {
            string content = TypeScriptSource.StringLiteralContent(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in TriadAffordanceFill.Matches(content))
            {
                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a shared UI primitive fills with a triad colour for affordance — primary/interactive fills cite "
            + "--color-accent, not the triad's --color-accent-active/-warning:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Fact 6 (plan.md §5.1). Resolves `--color-fg-on-fault` against
    /// `--color-accent-fault`, `-fault-hover` and `-fault-pressed` (all opaque
    /// `color-mix(in oklch, …)` derivations, plan.md §2.1) and asserts each pair
    /// is at least <see cref="MinimumTextContrast"/> (WCAG 1.4.3); resolves
    /// `--color-fg-disabled` against `--color-bg-base`/`-elevated`/`-raised` in
    /// every theme and asserts at least <see cref="MinimumNonTextContrast"/>
    /// (WCAG 1.4.11 / this spec's own choice, plan.md §2.3).
    ///
    /// <para>
    /// Red on develop: none of the three fault roles is declared in
    /// <c>tokens.css</c> yet. The disabled/surface pairs already meet their
    /// threshold today (plan.md §2.3's measured table) — this fact still red's
    /// on the missing fault roles, not on those.
    /// </para>
    /// </summary>
    [Fact]
    public void Fault_label_and_disabled_label_contrast_hold()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        File.Exists(tokenFile.FullName).ShouldBeTrue($"expected {tokenFile.FullName}.");

        string css = StripCssComments(File.ReadAllText(tokenFile.FullName));
        List<Declaration> declarations = ParseDeclarations(css);

        Dictionary<string, string> rootMap = declarations
            .Where(declaration => declaration.Selector.Trim() == ":root")
            .ToDictionary(declaration => declaration.Name, declaration => declaration.Value, StringComparer.Ordinal);

        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);
        Dictionary<string, string> highContrastMap = ThemeMap(declarations, rootMap, IsHighContrastTheme);

        List<string> problems = [];

        foreach (string fillName in new[] { "--color-accent-fault", "--color-accent-fault-hover", "--color-accent-fault-pressed" })
        {
            if (!rootMap.ContainsKey(fillName))
            {
                problems.Add($"{tokenFile.Name} does not declare {fillName} (spec 268 plan.md §2.1).");
                continue;
            }

            if (!rootMap.ContainsKey("--color-fg-on-fault"))
            {
                problems.Add($"{tokenFile.Name} does not declare --color-fg-on-fault (spec 268 plan.md §2.1).");
                continue;
            }

            double ratio = ContrastRatio(
                ToSrgb(ResolveOklch("--color-fg-on-fault", rootMap, [])),
                ToSrgb(ResolveOklch(fillName, rootMap, [])));

            if (ratio < MinimumTextContrast)
            {
                problems.Add(
                    $"--color-fg-on-fault on {fillName} is {ratio:F2}:1, below the {MinimumTextContrast}:1 "
                    + "WCAG 1.4.3 threshold for label text.");
            }
        }

        foreach ((string themeName, Dictionary<string, string> map) in new[]
                 {
                     ("dark", rootMap),
                     ("light", lightMap),
                     ("high-contrast", highContrastMap),
                 })
        {
            if (!map.ContainsKey("--color-fg-disabled"))
            {
                problems.Add($"[{themeName}] {tokenFile.Name} does not declare --color-fg-disabled.");
                continue;
            }

            foreach (string surface in new[] { "--color-bg-base", "--color-bg-elevated", "--color-bg-raised" })
            {
                if (!map.ContainsKey(surface))
                {
                    problems.Add($"[{themeName}] {tokenFile.Name} does not declare {surface}.");
                    continue;
                }

                double ratio = ContrastRatio(
                    ToSrgb(ResolveOklch("--color-fg-disabled", map, [])),
                    ToSrgb(ResolveOklch(surface, map, [])));

                if (ratio < MinimumNonTextContrast)
                {
                    problems.Add(
                        $"[{themeName}] --color-fg-disabled on {surface} is {ratio:F2}:1, below the "
                        + $"{MinimumNonTextContrast}:1 threshold this spec chose for a legible disabled label.");
                }
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    // =====================================================================
    // File scanning (mirrors SharedUiTokenUsageTests / DesignTokenLayerTests'
    // shape, over apps/*/src rather than one subtree).
    // =====================================================================

    /// <summary>Every non-test <c>.ts</c>/<c>.tsx</c> file under both apps' and shared's <c>src</c>.</summary>
    private static IEnumerable<string> ScannedFiles(DirectoryInfo root)
    {
        string[] scannedTrees = ["apps/management-web/src", "apps/kiosk-web/src", "apps/shared/src"];

        foreach (string tree in scannedTrees)
        {
            foreach (string relative in ScannedFilesUnder(root, Path.Combine(root.FullName, tree)))
            {
                yield return relative;
            }
        }
    }

    /// <summary>Every non-test <c>.ts</c>/<c>.tsx</c> file under one absolute directory.</summary>
    private static IEnumerable<string> ScannedFilesUnder(DirectoryInfo root, string absoluteDirectory)
    {
        if (!Directory.Exists(absoluteDirectory))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(absoluteDirectory, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".tsx", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = RepositorySource.RelativePath(root, file);
            if (relative.Contains(".test.", StringComparison.Ordinal) || relative.Contains(".spec.", StringComparison.Ordinal))
            {
                continue;
            }

            yield return relative;
        }
    }

    // =====================================================================
    // tokens.css: declaration parsing and per-theme maps (fact 6 only).
    // =====================================================================

    private static FileInfo TokenFile(DirectoryInfo root) =>
        new(Path.Combine(root.FullName, "apps/shared/src/ui/tokens/tokens.css"));

    private static Dictionary<string, string> ThemeMap(
        List<Declaration> declarations, Dictionary<string, string> rootMap, Func<string, bool> isTheme)
    {
        Dictionary<string, string> map = new(rootMap, StringComparer.Ordinal);

        foreach (Declaration declaration in declarations.Where(declaration => isTheme(declaration.Selector)))
        {
            map[declaration.Name] = declaration.Value;
        }

        return map;
    }

    private static bool IsLightTheme(string selector) =>
        Regex.IsMatch(selector, @"\[data-theme\s*=\s*['""]light['""]\]");

    private static bool IsHighContrastTheme(string selector) =>
        Regex.IsMatch(selector, @"\[data-theme\s*=\s*['""]high-contrast['""]\]");

    private static List<Declaration> ParseDeclarations(string css)
    {
        List<Declaration> declarations = [];

        foreach (Match block in Regex.Matches(css, @"(?<sel>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Singleline))
        {
            string selector = block.Groups["sel"].Value.Trim();

            foreach (Match declaration in Regex.Matches(
                         block.Groups["body"].Value,
                         @"(?<name>--[A-Za-z][A-Za-z0-9-]*)\s*:\s*(?<value>[^;]+);"))
            {
                declarations.Add(new Declaration(
                    selector,
                    declaration.Groups["name"].Value,
                    declaration.Groups["value"].Value.Trim()));
            }
        }

        return declarations;
    }

    private static string StripCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private sealed record Declaration(string Selector, string Name, string Value);

    // =====================================================================
    // OKLCH resolution through a var()/color-mix() chain, and WCAG contrast.
    // Extends OklchColorResolver (lifted, T001) to opaque color-mix() pairs —
    // new behaviour, not part of the lift.
    // =====================================================================

    private readonly record struct OklchTriple(double Lightness, double Chroma, double Hue);

    private static OklchTriple ResolveOklch(string name, Dictionary<string, string> map, HashSet<string> seen)
    {
        map.TryGetValue(name, out string? value).ShouldBeTrue($"{name} has no declaration to resolve.");
        string trimmed = value.ShouldNotBeNull().Trim();

        Match varOnly = Regex.Match(trimmed, @"^var\((?<name>--[A-Za-z0-9-]+)\)$");
        if (varOnly.Success)
        {
            string next = varOnly.Groups["name"].Value;
            seen.Add(name).ShouldBeTrue($"circular var() reference resolving {name} -> {next}.");
            return ResolveOklch(next, map, seen);
        }

        Match mix = Regex.Match(
            trimmed,
            @"^color-mix\(in oklch,\s*var\((?<a>--[A-Za-z0-9-]+)\)\s+(?<pct>\d+(?:\.\d+)?)%,\s*"
            + @"(?:var\((?<b>--[A-Za-z0-9-]+)\)|(?<transparent>transparent))\)$");
        if (mix.Success)
        {
            if (mix.Groups["transparent"].Success)
            {
                throw new InvalidOperationException(
                    $"{name} mixes toward transparent — this resolver serves fact 6's opaque pairs only.");
            }

            double fraction = double.Parse(mix.Groups["pct"].Value, CultureInfo.InvariantCulture) / 100.0;
            OklchTriple a = ResolveOklch(mix.Groups["a"].Value, map, []);
            OklchTriple b = ResolveOklch(mix.Groups["b"].Value, map, []);

            // color-mix(in oklch, ...) interpolates lightness and chroma
            // linearly; hue is taken from whichever operand carries chroma —
            // the other is typically black/white, whose hue is undefined
            // (plan.md §5.1 fact 6).
            double hue = a.Chroma >= b.Chroma ? a.Hue : b.Hue;
            return new OklchTriple(
                (fraction * a.Lightness) + ((1 - fraction) * b.Lightness),
                (fraction * a.Chroma) + ((1 - fraction) * b.Chroma),
                hue);
        }

        Match literal = Regex.Match(trimmed, @"^oklch\(\s*(?<l>[\d.]+)%\s+(?<c>[\d.]+)\s+(?<h>[\d.]+)\s*\)$");
        literal.Success.ShouldBeTrue(
            $"cannot resolve '{name}' — its value '{trimmed}' is not a var(), an opaque color-mix(), or an "
            + "oklch() literal.");

        return new OklchTriple(
            double.Parse(literal.Groups["l"].Value, CultureInfo.InvariantCulture),
            double.Parse(literal.Groups["c"].Value, CultureInfo.InvariantCulture),
            double.Parse(literal.Groups["h"].Value, CultureInfo.InvariantCulture));
    }

    private static (int R, int G, int B) ToSrgb(OklchTriple triple) =>
        OklchColorResolver.OklchToSrgb(triple.Lightness, triple.Chroma, triple.Hue);

    /// <summary>WCAG relative luminance (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance).</summary>
    private static double RelativeLuminance((int R, int G, int B) rgb)
    {
        double Channel(int value8Bit)
        {
            double c = value8Bit / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(rgb.R)) + (0.7152 * Channel(rgb.G)) + (0.0722 * Channel(rgb.B));
    }

    /// <summary>WCAG contrast ratio (https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio).</summary>
    private static double ContrastRatio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        double luminanceA = RelativeLuminance(a);
        double luminanceB = RelativeLuminance(b);
        double lighter = Math.Max(luminanceA, luminanceB);
        double darker = Math.Min(luminanceA, luminanceB);

        return (lighter + 0.05) / (darker + 0.05);
    }
}
