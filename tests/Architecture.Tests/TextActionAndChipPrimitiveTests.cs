using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards spec 427 (issue #2668): an underlined text action is the design-system
/// <c>LinkButton</c>, not a raw <c>&lt;button className="underline"&gt;</c> that re-spells the
/// look and misses the focus ring. A hit outside <c>LinkButton.tsx</c> is a violation.
///
/// <para>
/// Source-scanning over comment-stripped, non-test <c>apps/*/src/**/*.ts</c> and <c>*.tsx</c> (line numbers
/// preserved so a violation names a real <c>file:line</c>), paths forward-slashed through
/// <see cref="RepositorySource.RelativePath"/> so the guard reads the same on a Windows machine
/// and on Linux CI.
/// </para>
///
/// <para>
/// <b>Phase 4a (ADR-0139):</b> <see cref="An_underlined_text_action_is_a_LinkButton"/> is RED on
/// the tree before the migration, naming the six lines that hold the seven underlined elements
/// (spec 427 §1.1 L1-L7; <c>ChainRecoveryNotice</c>'s Retry and Reload share one class string).
/// <see cref="Each_allowlist_entry_still_applies"/> and <see cref="The_scan_reads_application_source"/>
/// are green in advance and are proved by a planted counterfactual.
/// </para>
///
/// <para>
/// Spec 427 US3 adds <see cref="A_selected_chip_is_a_ToggleChip"/>: a bare <c>bg-accent-subtle</c>
/// outside <c>ToggleChip.tsx</c> is a violation unless allowlisted with a reason. RED before the
/// filters migrate, naming <c>LayoutsPage</c>, <c>OverlaysPage</c> and <c>SystemVariablesPage</c>.
/// </para>
/// </summary>
public class TextActionAndChipPrimitiveTests
{
    private const string LinkButtonPath = "apps/shared/src/ui/primitives/LinkButton.tsx";

    private const string ToggleChipPath = "apps/shared/src/ui/primitives/ToggleChip.tsx";

    private static readonly string[] SourceExtensions = ["*.tsx", "*.ts"];

    private static readonly Regex UnderlineUtility = new(
        @"(?<![\w-])underline(?![\w-])",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex SelectedChipFill = new(
        @"(?<!data-\[highlighted\]:)(?<![\w-])bg-accent-subtle(?![\w/-])",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ToggleChipOpening = new(
        @"<ToggleChip\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The bare <c>bg-accent-subtle</c> lines that stay hand-spelled. Each <c>Match</c> is a
    /// distinctive substring of the line, so the entry stops applying when the line goes.
    /// Shrink-only.
    /// </summary>
    private static readonly (string RelativePath, string Match, string Reason)[] ChipAllowlist =
    [
        (
            "apps/management-web/src/app/ShellLayout.tsx",
            "font-medium text-accent",
            "spec 427 §1.1: the active NavItem is a NavLink marked aria-current, a link and not a toggle."),
        (
            "apps/management-web/src/features/layouts/GridDesigner.tsx",
            "relative rounded-md border border-accent",
            "spec 427 §1.1: the grid preset is a native radio group under a styled label (spec 228 FR-001: "
            + "roving tab stop and arrow keys from the platform); a ToggleChip would regress it."),
    ];

    [Fact]
    public void An_underlined_text_action_is_a_LinkButton()
    {
        var root = RepositorySource.Root();
        List<string> violations = [];

        foreach (var relativePath in ScannedSourceFiles(root))
        {
            if (relativePath == LinkButtonPath)
            {
                continue;
            }

            violations.AddRange(
                Hits(root, relativePath, UnderlineUtility).Select(line => $"{relativePath}:{line}"));
        }

        violations.ShouldBeEmpty(
            "a hand-spelled `underline` re-spells a text action the shared LinkButton already draws "
            + "(and gives it the design-system focus ring). A text action that is a button is a "
            + "<LinkButton> (import from '@smart-sentinel-eye/shared/ui/primitives/LinkButton', never the "
            + "ui barrel); a navigation link is not a button and belongs in a link primitive or an `<a>`: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Spec 427 (issue #2668) US3, G2. A selected state-filter chip is the design-system
    /// <c>ToggleChip</c>, which also announces <c>aria-pressed</c>. A bare
    /// <c>bg-accent-subtle</c> outside <c>ToggleChip.tsx</c> is a violation unless the line is
    /// allowlisted below, with a reason. The pattern excludes only the <c>data-[highlighted]:</c> variant
    /// (<c>data-[highlighted]:bg-accent-subtle</c> in the command palette and menus), which are
    /// not selected chips.
    /// </summary>
    [Fact]
    public void A_selected_chip_is_a_ToggleChip()
    {
        var root = RepositorySource.Root();
        List<string> violations = [];

        foreach (var relativePath in ScannedSourceFiles(root))
        {
            if (relativePath == ToggleChipPath)
            {
                continue;
            }

            foreach (var (line, text) in HitLines(root, relativePath, SelectedChipFill))
            {
                var allowlisted = ChipAllowlist.Any(entry =>
                    entry.RelativePath == relativePath
                    && text.Contains(entry.Match, StringComparison.Ordinal));

                if (!allowlisted)
                {
                    violations.Add($"{relativePath}:{line}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a hand-spelled selected chip (`bg-accent-subtle`) re-spells the control the shared ToggleChip "
            + "already draws, and announces no pressed state. Use <ToggleChip pressed={...}> (import from "
            + "'@smart-sentinel-eye/shared/ui/primitives/ToggleChip', never the ui barrel), or add a reasoned "
            + "allowlist entry if it is not a toggle: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    /// <summary>
    /// Spec 432 (issue #3083): a row of filter chips is a named group, so a screen-reader user hears
    /// which control the chips belong to. Each <c>&lt;ToggleChip</c> outside the primitive needs a
    /// line with both <c>role="group"</c> and <c>aria-label=</c> among the three lines above it.
    /// </summary>
    [Fact]
    public void A_ToggleChip_sits_inside_a_labelled_group()
    {
        var root = RepositorySource.Root();
        List<string> violations = [];

        foreach (var relativePath in ScannedSourceFiles(root).Where(path => path != ToggleChipPath))
        {
            var lines = StripCommentsKeepingLines(File.ReadAllText(Path.Combine(root.FullName, relativePath)))
                .Split('\n');

            foreach (var (line, _) in HitLines(root, relativePath, ToggleChipOpening))
            {
                var hasGroup = lines
                    .Skip(Math.Max(0, line - 4))
                    .Take(Math.Min(3, line - 1))
                    .Any(text =>
                        text.Contains("role=\"group\"", StringComparison.Ordinal)
                        && text.Contains("aria-label=", StringComparison.Ordinal));

                if (!hasGroup)
                {
                    violations.Add($"{relativePath}:{line}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "A <ToggleChip> row must sit in a container that carries role=\"group\" and an aria-label "
            + "(e.g. aria-label=\"Filter by state\") within the three lines above it, as RulesPage does: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    [Fact]
    public void Each_allowlist_entry_still_applies()
    {
        var root = RepositorySource.Root();
        List<string> problems = [];

        foreach (var primitivePath in new[] { LinkButtonPath, ToggleChipPath })
        {
            if (!File.Exists(Path.Combine(root.FullName, primitivePath)))
            {
                problems.Add($"{primitivePath} no longer exists — the one file allowed to spell its utility moved.");
            }
        }

        foreach ((var relativePath, var match, var reason) in ChipAllowlist)
        {
            if (reason.Length == 0)
            {
                problems.Add($"{relativePath} ({match}) has no reason recorded.");
            }

            if (!File.Exists(Path.Combine(root.FullName, relativePath)))
            {
                problems.Add($"{relativePath} no longer exists — remove its allowlist entry.");
                continue;
            }

            var matchingLines = HitLines(root, relativePath, SelectedChipFill)
                .Count(hit => hit.Text.Contains(match, StringComparison.Ordinal));

            if (matchingLines == 0)
            {
                problems.Add(
                    $"{relativePath} has no bare `bg-accent-subtle` line containing '{match}' — its allowlist "
                    + "entry no longer applies and must be removed (the list is shrink-only).");
            }
            else if (matchingLines > 1)
            {
                problems.Add(
                    $"{relativePath} has {matchingLines} bare `bg-accent-subtle` lines containing '{match}' — "
                    + "the entry must match exactly one line, or it silently excuses a second chip. Tighten the match.");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A guard that scans nothing passes everything. If an application tree moves or the filter
    /// over-excludes, this is the loud failure.
    /// </summary>
    [Fact]
    public void The_scan_reads_application_source()
    {
        var root = RepositorySource.Root();
        var scanned = ScannedSourceFiles(root);

        foreach (var sourceFolder in new[] { "apps/shared/src/", "apps/management-web/src/" })
        {
            scanned.ShouldContain(
                relative => relative.StartsWith(sourceFolder, StringComparison.Ordinal),
                $"no non-test .ts/.tsx file was found under {sourceFolder}; every other fact here would pass vacuously.");
        }
    }

    private static List<string> ScannedSourceFiles(DirectoryInfo root) =>
        [
            .. SourceExtensions
                .SelectMany(extension => Directory.EnumerateFiles(
                    Path.Combine(root.FullName, "apps"),
                    extension,
                    SearchOption.AllDirectories))
                .Select(file => RepositorySource.RelativePath(root, file))
                .Where(relative =>
                    Regex.IsMatch(relative, @"^apps/[^/]+/src/", RegexOptions.None, TimeSpan.FromSeconds(5))
                    && !relative.Contains("/node_modules/", StringComparison.Ordinal)
                    && !relative.Contains(".test.", StringComparison.Ordinal)
                    && !relative.Contains(".spec.", StringComparison.Ordinal)
                    && !relative.Contains("/test/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

    private static List<(int Line, string Text)> HitLines(DirectoryInfo root, string relativePath, Regex pattern)
    {
        var lines = StripCommentsKeepingLines(File.ReadAllText(Path.Combine(root.FullName, relativePath)))
            .Split('\n');

        return
        [
            .. lines
                .Select((text, index) => (Line: index + 1, Text: text))
                .Where(line => pattern.IsMatch(line.Text)),
        ];
    }

    private static List<int> Hits(DirectoryInfo root, string relativePath, Regex pattern)
    {
        var source = StripCommentsKeepingLines(File.ReadAllText(Path.Combine(root.FullName, relativePath)));

        return
        [
            .. pattern
                .Matches(source)
                .Select(match => source.AsSpan(0, match.Index).Count('\n') + 1)
                .Distinct(),
        ];
    }

    /// <summary>Comments blanked, newlines kept, so a match's line number is the file's.</summary>
    private static string StripCommentsKeepingLines(string text)
    {
        var withoutBlock = Regex.Replace(
            text,
            @"/\*.*?\*/",
            comment => new string('\n', comment.Value.Count(character => character == '\n')),
            RegexOptions.Singleline);

        return Regex.Replace(withoutBlock, @"(?<![:'""`])//[^\n]*", string.Empty);
    }
}
