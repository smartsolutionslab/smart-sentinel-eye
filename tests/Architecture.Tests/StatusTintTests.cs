using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US0 of spec 297 (issue #2635): each status triad's translucent-tint
/// role is a primitive stop at the triad's own hue, not a <c>color-mix(in
/// oklch, ...)</c> derivation. <c>color-mix(in oklch, ...)</c> interpolates
/// hue linearly by the mix percentage, so a small mix into a ground that
/// itself carries a real hue renders the ground's hue, not the triad's — a
/// slate tint instead of a red one, observed in Chromium 1243 against this
/// tree's own <c>--color-accent-fault-subtle</c> (spec §1, T001/V1: canvas
/// read-back <c>rgb(20,24,37)</c>). <c>DesignTokenLayerTests</c> and
/// <c>InteractionStateTests</c> guard the rest of the token file's shape and
/// contrast; this class is scoped to the three <c>-subtle</c> tint roles and,
/// since spec 299, the three <c>-text</c> roles beside them.
///
/// <para>
/// Reuses <c>DesignTokenLayerTests</c>' declaration-parser shape and
/// <c>InteractionStateTests</c>' OKLCH/contrast pattern, copied rather than
/// shared (plan.md §5.1: "if the parser is private, copy the minimum rather
/// than refactor a guard" — a lift is a separate, characterised change).
/// Locates the token file by the same fixed path
/// <c>InteractionStateTests</c> uses, not by following an app's
/// <c>@import</c> (that resolution already has exactly one owner,
/// <c>DesignTokenLayerTests</c>).
/// </para>
///
/// <para>
/// <b>Spec 299 (issue #2695):</b> the triad as text on its tint and on the
/// plain grounds is below 4.5:1 in the light theme — not excused by a
/// shrink-only carve-out any more. Each triad hue now gets a per-theme
/// <b>text</b> role, <c>--color-accent-&lt;role&gt;-text</c> (ADR-0146's
/// 2026-10-04 amendment): the signal itself in dark/high-contrast, a darker
/// stop at the same hue in light. The former carve-out —
/// <c>ContrastExclusions</c>, <c>IssueReference</c> and the honesty fact
/// <c>Each_contrast_exclusion_still_fails</c> that kept it from going
/// stale — is deleted outright (FR-009), not weakened: the bug it excused is
/// fixed, so there is nothing left to excuse.
/// </para>
///
/// <para>
/// Phase-4a colour: red (spec §6, this spec's own red facts layered on top
/// of spec 297's). Red on develop: <see cref="Each_triad_text_role_keeps_its_triad_hue"/>
/// and <see cref="A_triad_label_is_legible_on_every_ground"/> (the <c>-text</c>
/// roles are not declared yet) and <see cref="A_triad_label_is_legible_on_its_tint"/>
/// (now resolves the <c>-text</c> role and loops <c>light</c> too — both
/// unresolved/below threshold today). <see cref="Each_triad_tint_is_a_literal_at_its_triad_hue"/>,
/// <see cref="No_theme_redeclares_a_triad_role"/> and
/// <see cref="The_neutral_label_is_legible_on_its_fill"/> are spec 297's own
/// facts and must stay green, unmodified, throughout.
/// </para>
/// </summary>
public class StatusTintTests
{
    // Tolerance for "the same hue" (plan.md §5.1 fact 1) — the tint stops are
    // declared to the triad's hue to the hundredth (plan.md §2.1), so two
    // literals citing the same triad hue resolve identically; this only
    // needs to absorb floating-point round-trip noise, not real drift.
    private const double HueTolerance = 0.01;

    private const double MinimumTextContrast = 4.5; // WCAG 1.4.3.

    private static readonly string[] TriadRoles = ["active", "warning", "fault"];

    private static readonly Regex SimpleVarValue = new(@"^var\(--[A-Za-z0-9-]+\)$", RegexOptions.Compiled);

    private static readonly Regex VarReference = new(@"var\(\s*(?<name>--[A-Za-z0-9-]+)", RegexOptions.Compiled);

    private static readonly Regex OklchLiteral = new(
        @"^oklch\(\s*(?<l>[\d.]+)%\s+(?<c>[\d.]+)\s+(?<h>[\d.]+)\s*\)$", RegexOptions.Compiled);

    /// <summary>
    /// Fact 1 (plan.md §5.1). Red on develop (see class remarks): for each
    /// triad hue, in <c>:root</c> and in <c>light</c>, <c>--color-accent-
    /// &lt;role&gt;-subtle</c> must resolve <b>through <c>var()</c> only</b>
    /// to an <c>oklch(L C H)</c> literal whose <c>H</c> equals
    /// <c>--color-accent-&lt;role&gt;</c>'s own resolved <c>H</c>. A
    /// <c>color-mix</c> value — the shape <c>--color-accent-fault-subtle</c>
    /// has today — fails naming the mix, not a hue mismatch.
    /// </summary>
    [Fact]
    public void Each_triad_tint_is_a_literal_at_its_triad_hue()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        Dictionary<string, string> rootMap = RootMap(declarations);
        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);

        List<string> problems = [];

        foreach (string role in TriadRoles)
        {
            string triadName = $"--color-accent-{role}";
            (bool triadOk, OklchTriple? triadValue, string? triadReason) = ResolveLiteralThroughVarOnly(triadName, rootMap);

            if (!triadOk)
            {
                problems.Add($"{triadName}: {triadReason}");
                continue;
            }

            foreach ((string themeName, Dictionary<string, string> map) in new[] { ("dark", rootMap), ("light", lightMap) })
            {
                string subtleName = $"--color-accent-{role}-subtle";
                (bool ok, OklchTriple? value, string? reason) = ResolveLiteralThroughVarOnly(subtleName, map);

                if (!ok)
                {
                    problems.Add($"[{themeName}] {subtleName}: {reason}");
                    continue;
                }

                double diff = Math.Abs(value!.Value.Hue - triadValue!.Value.Hue);
                if (diff > HueTolerance)
                {
                    problems.Add(
                        $"[{themeName}] {subtleName} resolves to hue {value.Value.Hue}, but {triadName} resolves "
                        + $"to hue {triadValue.Value.Hue} — a tint must cite its own triad's hue exactly "
                        + $"(diff {diff:F4} > {HueTolerance}).");
                }
            }
        }

        problems.ShouldBeEmpty(
            $"{tokenFile.Name}'s triad tint roles are not literals at their triad's own hue:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => $"  {p}")));
    }

    /// <summary>
    /// New fact (spec 299 plan.md §4.3, issue #2695). Fact 1's shape, applied
    /// to the <c>-text</c> role instead of the <c>-subtle</c> tint, over all
    /// three themes (dark, light, high-contrast — ADR-0146's 2026-10-04
    /// amendment permits the text role, and only the text role, to differ
    /// per theme). Red on develop: <c>--color-accent-&lt;role&gt;-text</c> is
    /// not declared anywhere yet.
    /// </summary>
    [Fact]
    public void Each_triad_text_role_keeps_its_triad_hue()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        Dictionary<string, string> rootMap = RootMap(declarations);
        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);
        Dictionary<string, string> highContrastMap = ThemeMap(declarations, rootMap, IsHighContrastTheme);

        List<string> problems = [];

        foreach (string role in TriadRoles)
        {
            string triadName = $"--color-accent-{role}";
            (bool triadOk, OklchTriple? triadValue, string? triadReason) = ResolveLiteralThroughVarOnly(triadName, rootMap);

            if (!triadOk)
            {
                problems.Add($"{triadName}: {triadReason}");
                continue;
            }

            foreach ((string themeName, Dictionary<string, string> map) in new[]
                     {
                         ("dark", rootMap),
                         ("light", lightMap),
                         ("high-contrast", highContrastMap),
                     })
            {
                string textName = $"--color-accent-{role}-text";
                (bool ok, OklchTriple? value, string? reason) = ResolveLiteralThroughVarOnly(textName, map);

                if (!ok)
                {
                    problems.Add($"[{themeName}] {textName}: {reason}");
                    continue;
                }

                double diff = Math.Abs(value!.Value.Hue - triadValue!.Value.Hue);
                if (diff > HueTolerance)
                {
                    problems.Add(
                        $"[{themeName}] {textName} resolves to hue {value.Value.Hue}, but {triadName} resolves "
                        + $"to hue {triadValue.Value.Hue} — a text role must cite its own triad's hue exactly "
                        + $"(diff {diff:F4} > {HueTolerance}).");
                }
            }
        }

        problems.ShouldBeEmpty(
            $"{tokenFile.Name}'s triad text roles are not at their triad's own hue:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => $"  {p}")));
    }

    /// <summary>
    /// Fact 2 (green pin). <c>--color-accent-active</c>,
    /// <c>--color-accent-warning</c> and <c>--color-accent-fault</c> are
    /// declared only in <c>:root</c> — true today, unrelated to this spec's
    /// change.
    /// </summary>
    [Fact]
    public void No_theme_redeclares_a_triad_role()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));

        string[] triadRoleNames = [.. TriadRoles.Select(role => $"--color-accent-{role}")];

        Declaration[] redeclared =
            [.. declarations.Where(d => triadRoleNames.Contains(d.Name, StringComparer.Ordinal) && !IsRootSelector(d.Selector))];

        redeclared.ShouldBeEmpty(
            $"{tokenFile.Name} redeclares a triad role outside :root: "
            + string.Join(", ", redeclared.Select(d => $"{d.Name} in {d.Selector}"))
            + " — the triad itself is pinned across themes (ADR-0148); only the -subtle tints may differ per theme.");
    }

    /// <summary>
    /// Fact 3 (plan.md §5.1/§4.3, amended by spec 299 issue #2695). Each
    /// triad's <b>text role</b> — not the signal colour directly any more —
    /// on its own <c>-subtle</c> tint is &gt;= 4.5:1 (WCAG 1.4.3) in
    /// <b>every</b> theme, <c>light</c> included. There is no exclusion list
    /// any more (FR-009): the text role is what fixes the light-theme
    /// failure the old carve-out used to excuse.
    /// </summary>
    [Fact]
    public void A_triad_label_is_legible_on_its_tint()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        Dictionary<string, string> rootMap = RootMap(declarations);
        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);
        Dictionary<string, string> highContrastMap = ThemeMap(declarations, rootMap, IsHighContrastTheme);

        List<string> problems = [];

        foreach ((string themeName, Dictionary<string, string> map) in new[]
                 {
                     ("dark", rootMap),
                     ("light", lightMap),
                     ("high-contrast", highContrastMap),
                 })
        {
            foreach (string role in TriadRoles)
            {
                string textName = $"--color-accent-{role}-text";
                string fillName = $"--color-accent-{role}-subtle";

                (double ratio, string? error) = TryContrastRatio(textName, fillName, map);
                if (error is not null)
                {
                    problems.Add($"[{themeName}] {error}");
                    continue;
                }

                if (ratio < MinimumTextContrast)
                {
                    problems.Add(
                        $"[{themeName}] {textName} on {fillName} is {ratio:F2}:1, below the "
                        + $"{MinimumTextContrast}:1 WCAG 1.4.3 threshold.");
                }
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// New fact (spec 299 plan.md §4.3, issue #2695). The issue named "any
    /// light surface", not only the tint: each triad's text role must also
    /// clear 4.5:1 on the three plain grounds (<c>--color-bg-base</c>,
    /// <c>-elevated</c>, <c>-raised</c>) in every theme. Red on develop: the
    /// text roles are not declared yet.
    /// </summary>
    [Fact]
    public void A_triad_label_is_legible_on_every_ground()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        Dictionary<string, string> rootMap = RootMap(declarations);
        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);
        Dictionary<string, string> highContrastMap = ThemeMap(declarations, rootMap, IsHighContrastTheme);

        List<string> problems = [];

        foreach ((string themeName, Dictionary<string, string> map) in new[]
                 {
                     ("dark", rootMap),
                     ("light", lightMap),
                     ("high-contrast", highContrastMap),
                 })
        {
            foreach (string role in TriadRoles)
            {
                string textName = $"--color-accent-{role}-text";

                foreach (string ground in new[] { "--color-bg-base", "--color-bg-elevated", "--color-bg-raised" })
                {
                    (double ratio, string? error) = TryContrastRatio(textName, ground, map);
                    if (error is not null)
                    {
                        problems.Add($"[{themeName}] {error}");
                        continue;
                    }

                    if (ratio < MinimumTextContrast)
                    {
                        problems.Add(
                            $"[{themeName}] {textName} on {ground} is {ratio:F2}:1, below the "
                            + $"{MinimumTextContrast}:1 WCAG 1.4.3 threshold.");
                    }
                }
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Fact 5 (green pin). <c>--color-fg-muted</c> on <c>--color-bg-raised</c>
    /// (the neutral tone, FR-008) is &gt;= 4.5:1 in every theme — true today,
    /// unrelated to this spec's change (dark: 4.64:1, plan.md §2.2).
    /// </summary>
    [Fact]
    public void The_neutral_label_is_legible_on_its_fill()
    {
        DirectoryInfo root = RepositorySource.Root();
        FileInfo tokenFile = TokenFile(root);
        List<Declaration> declarations = ParseDeclarations(ReadCss(tokenFile));
        Dictionary<string, string> rootMap = RootMap(declarations);
        Dictionary<string, string> lightMap = ThemeMap(declarations, rootMap, IsLightTheme);
        Dictionary<string, string> highContrastMap = ThemeMap(declarations, rootMap, IsHighContrastTheme);

        List<string> problems = [];

        foreach ((string themeName, Dictionary<string, string> map) in new[]
                 {
                     ("dark", rootMap),
                     ("light", lightMap),
                     ("high-contrast", highContrastMap),
                 })
        {
            (double ratio, string? error) = TryContrastRatio("--color-fg-muted", "--color-bg-raised", map);
            if (error is not null)
            {
                problems.Add($"[{themeName}] {error}");
                continue;
            }

            if (ratio < MinimumTextContrast)
            {
                problems.Add(
                    $"[{themeName}] --color-fg-muted on --color-bg-raised is {ratio:F2}:1, below the "
                    + $"{MinimumTextContrast}:1 WCAG 1.4.3 threshold.");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    // =====================================================================
    // Resolution: var()-only (fact 1) and var()/opaque-color-mix (facts 3-5).
    // Copied and adapted from InteractionStateTests' ResolveOklch (plan.md
    // §5.1: "copy the minimum rather than refactor a guard").
    // =====================================================================

    private readonly record struct OklchTriple(double Lightness, double Chroma, double Hue);

    /// <summary>
    /// Fact 1's strict resolver: follows <c>var()</c> hops only; a
    /// <c>color-mix</c> or anything else that is not a bare <c>var()</c> or a
    /// terminal <c>oklch()</c> literal is reported, not resolved.
    /// </summary>
    private static (bool Success, OklchTriple? Value, string? Reason) ResolveLiteralThroughVarOnly(
        string name, Dictionary<string, string> map)
    {
        string current = name;
        HashSet<string> seen = [];

        while (true)
        {
            if (!map.TryGetValue(current, out string? value))
            {
                return (false, null, current == name
                    ? $"{name} is not declared."
                    : $"{name} resolves to {current}, which is not declared.");
            }

            string trimmed = value.Trim();

            if (SimpleVarValue.IsMatch(trimmed))
            {
                if (!seen.Add(current))
                {
                    return (false, null, $"{name} has a circular var() reference resolving {current}.");
                }

                current = VarReference.Match(trimmed).Groups["name"].Value;
                continue;
            }

            if (trimmed.Contains("color-mix", StringComparison.Ordinal))
            {
                return (false, null,
                    $"resolves to '{trimmed}' (via {current}) — a color-mix renders the ground's hue in the "
                    + "browser (spec 297 §1); cite a tint stop instead.");
            }

            Match literal = OklchLiteral.Match(trimmed);
            if (!literal.Success)
            {
                return (false, null, $"resolves to '{trimmed}' (via {current}), neither var() nor an oklch() literal.");
            }

            return (true, new OklchTriple(
                double.Parse(literal.Groups["l"].Value, CultureInfo.InvariantCulture),
                double.Parse(literal.Groups["c"].Value, CultureInfo.InvariantCulture),
                double.Parse(literal.Groups["h"].Value, CultureInfo.InvariantCulture)), null);
        }
    }

    /// <summary>
    /// Facts 3-5's general resolver: follows <c>var()</c> hops and opaque
    /// <c>color-mix(in oklch, ...)</c> pairs down to sRGB — the shape
    /// <c>--color-accent-fault-subtle</c> has today, before F re-points it.
    /// </summary>
    private static (double Ratio, string? Error) TryContrastRatio(string textName, string fillName, Dictionary<string, string> map)
    {
        (bool textOk, (int R, int G, int B)? text, string? textError) = TryResolveSrgb(textName, map, []);
        if (!textOk)
        {
            return (0, $"{textName} {textError}");
        }

        (bool fillOk, (int R, int G, int B)? fill, string? fillError) = TryResolveSrgb(fillName, map, []);
        if (!fillOk)
        {
            return (0, $"{fillName} {fillError}");
        }

        return (ContrastRatio(text!.Value, fill!.Value), null);
    }

    private static (bool Success, (int R, int G, int B)? Value, string? Reason) TryResolveSrgb(
        string name, Dictionary<string, string> map, HashSet<string> seen)
    {
        if (!map.TryGetValue(name, out string? value))
        {
            return (false, null, "is not declared.");
        }

        string trimmed = value.Trim();

        if (SimpleVarValue.IsMatch(trimmed))
        {
            string next = VarReference.Match(trimmed).Groups["name"].Value;
            if (!seen.Add(name))
            {
                return (false, null, $"has a circular var() reference resolving {next}.");
            }

            return TryResolveSrgb(next, map, seen);
        }

        Match mix = Regex.Match(
            trimmed,
            @"^color-mix\(in (?<space>oklab|oklch),\s*var\((?<a>--[A-Za-z0-9-]+)\)\s+(?<pct>\d+(?:\.\d+)?)%,\s*"
            + @"(?:var\((?<b>--[A-Za-z0-9-]+)\)|(?<transparent>transparent))\)$");
        if (mix.Success)
        {
            if (mix.Groups["transparent"].Success)
            {
                return (false, null, $"resolves to '{trimmed}', which mixes toward transparent — no single rendered colour.");
            }

            (bool aOk, OklchTriple? a, string? aReason) = ResolveLiteralOrMix(mix.Groups["a"].Value, map);
            if (!aOk)
            {
                return (false, null, aReason);
            }

            (bool bOk, OklchTriple? b, string? bReason) = ResolveLiteralOrMix(mix.Groups["b"].Value, map);
            if (!bOk)
            {
                return (false, null, bReason);
            }

            double fraction = double.Parse(mix.Groups["pct"].Value, CultureInfo.InvariantCulture) / 100.0;

            // Evaluates the declared space exactly (spec 299 FR-003, issue
            // #2695) via OklchColorResolver.Mix, the same evaluator
            // InteractionStateTests.ResolveOklch calls — rather than
            // approximating the mixed hue from whichever operand carries
            // more chroma.
            OklchColorResolver.MixSpace space = mix.Groups["space"].Value == "oklab"
                ? OklchColorResolver.MixSpace.Oklab
                : OklchColorResolver.MixSpace.Oklch;

            OklchTriple aValue = a!.Value;
            OklchTriple bValue = b!.Value;

            (double lightness, double chroma, double hue) = OklchColorResolver.Mix(
                space, (aValue.Lightness, aValue.Chroma, aValue.Hue), fraction,
                (bValue.Lightness, bValue.Chroma, bValue.Hue));

            return (true, OklchColorResolver.OklchToSrgb(lightness, chroma, hue), null);
        }

        Match literal = OklchLiteral.Match(trimmed);
        if (literal.Success)
        {
            return (true, OklchColorResolver.OklchToSrgb(
                double.Parse(literal.Groups["l"].Value, CultureInfo.InvariantCulture),
                double.Parse(literal.Groups["c"].Value, CultureInfo.InvariantCulture),
                double.Parse(literal.Groups["h"].Value, CultureInfo.InvariantCulture)), null);
        }

        return (false, null, $"resolves to '{trimmed}', which this resolver cannot parse (not var(), opaque color-mix(), or an oklch() literal).");
    }

    /// <summary>
    /// A <c>color-mix</c>'s operand, resolved as a var()-chain to an oklch()
    /// literal — the mix's two operands (a triad role, a ground) are always
    /// this shape, never a nested mix, so the strict resolver serves both.
    /// </summary>
    private static (bool Success, OklchTriple? Value, string? Reason) ResolveLiteralOrMix(string name, Dictionary<string, string> map) =>
        ResolveLiteralThroughVarOnly(name, map) switch
        {
            (true, OklchTriple value, _) => (true, value, null),
            (false, _, string literalReason) => (false, null, $"{name}: {literalReason}"),
            _ => (false, null, $"{name}: could not resolve."),
        };

    private static double RelativeLuminance((int R, int G, int B) rgb)
    {
        double Channel(int value8Bit)
        {
            double c = value8Bit / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(rgb.R)) + (0.7152 * Channel(rgb.G)) + (0.0722 * Channel(rgb.B));
    }

    private static double ContrastRatio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        double luminanceA = RelativeLuminance(a);
        double luminanceB = RelativeLuminance(b);
        double lighter = Math.Max(luminanceA, luminanceB);
        double darker = Math.Min(luminanceA, luminanceB);

        return (lighter + 0.05) / (darker + 0.05);
    }

    // =====================================================================
    // tokens.css: declaration parsing and per-theme maps. Copied from
    // InteractionStateTests (plan.md §5.1: "copy the minimum").
    // =====================================================================

    private static FileInfo TokenFile(DirectoryInfo root) =>
        new(Path.Combine(root.FullName, "apps/shared/src/ui/tokens/tokens.css"));

    private static string ReadCss(FileInfo tokenFile) => StripCssComments(File.ReadAllText(tokenFile.FullName));

    private static string StripCssComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private sealed record Declaration(string Selector, string Name, string Value);

    private static Dictionary<string, string> RootMap(List<Declaration> declarations) =>
        declarations
            .Where(declaration => IsRootSelector(declaration.Selector))
            .ToDictionary(declaration => declaration.Name, declaration => declaration.Value, StringComparer.Ordinal);

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

    private static bool IsRootSelector(string selector) => selector.Trim() == ":root";

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
}
