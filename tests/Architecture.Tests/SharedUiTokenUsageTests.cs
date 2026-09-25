using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US3 of spec 256 (issue #2332): the shared UI primitives and composites
/// that no later issue owns cite the semantic token layer for every colour,
/// never a Tailwind stock-palette utility, a call-site alpha modifier on a
/// semantic colour, or a raw colour literal.
///
/// <para>
/// Scans <c>apps/shared/src/ui/**/*.{ts,tsx}</c>, excluding <c>*.test.*</c> and
/// anything under <c>tokens/</c> (the token file is where a primitive belongs).
/// Comments are stripped first — a <c>#2342</c> issue reference in a comment
/// reads like a hex colour to a naive scan — and each rule is applied only to the
/// content of string literals (single, double, template), never to identifiers
/// or JSX structure (plan.md §5.2).
/// </para>
///
/// <para>
/// <b>The carve-out list is exactly four files</b>, all owned by #2342
/// ("gated on #2332", spec §3): <c>OverlayEditor.tsx</c>,
/// <c>BackdropControls.tsx</c>, <c>OverlayGeometryFields.tsx</c> and
/// <c>overlayLabelStyle.ts</c>. <see cref="Each_carve_out_still_exists_and_still_violates"/>
/// keeps the list honest — a file #2342 has already converted must leave it
/// rather than sit there unused, so this is a shrink-only guard.
/// </para>
///
/// <para>
/// Measured on develop (spec 256 plan.md §5.2): the violators outside the
/// carve-outs are <c>CameraViewer.tsx</c> (2 stock-palette), <c>DataTable.tsx</c>
/// (2 alpha), <c>Dialog.tsx</c> (1 stock-palette), <c>ConfirmDialog.tsx</c> (1
/// stock-palette) and <c>Tooltip.tsx</c> (1 alpha) — exactly US3's migration set.
/// <see cref="No_colour_literal"/> has no violator today: nothing outside the
/// carve-outs contains a raw hex/rgb/hsl/oklch literal, so it is green on
/// develop, not because the rule is empty, but because this particular
/// violation does not occur in shared UI yet (the carve-outs are the only
/// files that do it, and they are excluded by design).
/// </para>
/// </summary>
public class SharedUiTokenUsageTests
{
    private const string SharedUiRoot = "apps/shared/src/ui";

    /// <summary>
    /// The shrink-only carve-out list. Each entry names the issue that owns
    /// converting it (#2342) and why.
    /// </summary>
    private static readonly (string RelativePath, string Reason)[] CarveOuts =
    [
        (
            "apps/shared/src/ui/composites/OverlayEditor.tsx",
            "#2342: the overlay editor's canvas backdrop and drag-frame styling are inline "
            + "CSSProperties, not Tailwind classes, and #2342 owns converting the whole file in one pass."),
        (
            "apps/shared/src/ui/composites/BackdropControls.tsx",
            "#2342: its own header comment says a half-converted file is worse than none — the "
            + "notice/alert inline colours wait for #2342's single pass."),
        (
            "apps/shared/src/ui/composites/OverlayGeometryFields.tsx",
            "#2342: the field alert/status inline colours are part of the same overlay-editor "
            + "inline-style surface #2342 owns."),
        (
            "apps/shared/src/ui/composites/overlayLabelStyle.ts",
            "#2342: the label's rendered look over live video on both surfaces (spec 146) — on the "
            + "render leg, deliberately not touched by spec 256 (spec §3, §5)."),
    ];

    private static readonly Regex StockPaletteUtility = new(
        @"\b(?:bg|text|border|ring|fill|stroke|outline|divide)-(?:black|white|slate|gray|zinc|neutral|stone|red"
        + @"|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)\b",
        RegexOptions.Compiled);

    private static readonly Regex CallSiteAlphaOnSemantic = new(
        @"\b(?:bg|text|border|ring|fill|stroke|outline|divide)-(?:bg|fg|accent|border|focus)-[a-z-]*/\d+",
        RegexOptions.Compiled);

    private static readonly Regex ColourLiteral = new(
        @"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|oklch\(",
        RegexOptions.Compiled);

    /// <summary>Red on develop: Dialog.tsx, ConfirmDialog.tsx and CameraViewer.tsx use <c>bg-black</c>.</summary>
    [Fact]
    public void No_stock_palette_colour_utility()
    {
        AssertNoneOutsideCarveOuts(
            StockPaletteUtility,
            "a Tailwind stock-palette colour utility (bg-black, text-white, border-gray-700, ...)");
    }

    /// <summary>Red on develop: DataTable.tsx and Tooltip.tsx use <c>border-fg-muted/NN</c>.</summary>
    [Fact]
    public void No_call_site_alpha_on_a_semantic_colour()
    {
        AssertNoneOutsideCarveOuts(
            CallSiteAlphaOnSemantic,
            "call-site alpha on a semantic colour (e.g. border-fg-muted/30)");
    }

    /// <summary>
    /// Green on develop (vacuous, see class remarks): no hex/rgb()/hsl()/oklch()
    /// literal appears in shared UI outside the carve-outs today.
    /// </summary>
    [Fact]
    public void No_colour_literal()
    {
        AssertNoneOutsideCarveOuts(
            ColourLiteral,
            "a colour literal (hex, rgb()/rgba(), hsl()/hsla(), oklch())");
    }

    /// <summary>Green: the four carve-out files exist and each still violates at least one rule.</summary>
    [Fact]
    public void Each_carve_out_still_exists_and_still_violates()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> problems = [];

        foreach ((string relativePath, string reason) in CarveOuts)
        {
            reason.Contains("#2342", StringComparison.Ordinal).ShouldBeTrue(
                $"{relativePath}'s carve-out entry must name #2342, the issue that owns converting it.");

            string path = Path.Combine(root.FullName, relativePath);
            if (!File.Exists(path))
            {
                problems.Add($"{relativePath} no longer exists — remove it from the carve-out list.");
                continue;
            }

            string literalContent = StringLiteralContent(File.ReadAllText(path));
            bool stillViolates = StockPaletteUtility.IsMatch(literalContent)
                || CallSiteAlphaOnSemantic.IsMatch(literalContent)
                || ColourLiteral.IsMatch(literalContent);

            if (!stillViolates)
            {
                problems.Add(
                    $"{relativePath} no longer violates any rule — #2342 must have converted it, so it has to "
                    + "leave the carve-out list rather than sit there unused (a converted file must leave the "
                    + "list).");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    private static void AssertNoneOutsideCarveOuts(Regex rule, string description)
    {
        DirectoryInfo root = RepositorySource.Root();
        HashSet<string> carveOutPaths = [.. CarveOuts.Select(carveOut => carveOut.RelativePath)];

        List<string> violators = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            if (carveOutPaths.Contains(relativePath))
            {
                continue;
            }

            string path = Path.Combine(root.FullName, relativePath);
            string literalContent = StringLiteralContent(File.ReadAllText(path));

            if (rule.IsMatch(literalContent))
            {
                violators.Add(relativePath);
            }
        }

        violators.ShouldBeEmpty(
            $"{violators.Count} file(s) under {SharedUiRoot} use {description}, outside the carve-out list: "
            + string.Join(", ", violators)
            + " — cite the semantic token layer instead, or add a reasoned, issue-tagged carve-out entry.");
    }

    private static IEnumerable<string> ScannedFiles(DirectoryInfo root)
    {
        string full = Path.Combine(root.FullName, SharedUiRoot);

        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".tsx", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = RepositorySource.RelativePath(root, file);

            if (relative.Contains(".test.", StringComparison.Ordinal))
            {
                continue;
            }

            if (relative.Contains("/tokens/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return relative;
        }
    }

    /// <summary>
    /// The content of every single-, double- or template-quoted string literal in
    /// the file, comments stripped first. The rules apply only here — never to
    /// identifiers or JSX structure (plan.md §5.2).
    /// </summary>
    private static string StringLiteralContent(string source)
    {
        string withoutBlockComments = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        string withoutComments = Regex.Replace(withoutBlockComments, @"//[^\n]*", string.Empty);

        List<string> literals = [];

        foreach (Match match in Regex.Matches(
                     withoutComments,
                     @"'(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*""|`(?:[^`\\]|\\.)*`",
                     RegexOptions.Singleline))
        {
            literals.Add(match.Value[1..^1]);
        }

        return string.Join('\n', literals);
    }
}
