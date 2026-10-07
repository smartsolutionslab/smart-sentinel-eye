using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US2 of spec 297 (issue #2635), ADR-0146: the wall's three status
/// chips (<c>LiveUpdatesBadge</c>, <c>TileAlignmentBadge</c>, the *Overlay
/// unavailable* chip in <c>LayoutGrid.tsx</c>) render an opaque fill, not a
/// translucent call-site alpha modifier over live video — and, widened by
/// issue #2746, no other wall file paints a triad colour (solid or
/// translucent) as affordance or brand, the second half of ADR-0146's own
/// sentence ("a triad colour (signal or text) used for affordance or
/// brand").
///
/// <para>
/// Shape copied from <see cref="SharedUiTokenUsageTests"/> (the string-literal
/// scan via <see cref="TypeScriptSource"/>) and its allowlist shape from
/// <see cref="MotionLanguageTests"/> (<c>(RelativePath, Match, Reason)</c>,
/// kept honest by <see cref="Each_wall_allowlist_entry_still_matches"/>).
/// Scoped to <c>apps/kiosk-web/src</c> only —
/// <c>apps/shared/src/ui</c> is already held by
/// <see cref="SharedUiTokenUsageTests.No_call_site_alpha_on_a_semantic_colour"/>,
/// so together the two guards cover the kiosk's whole UI reach (plan.md
/// §5.2).
/// </para>
///
/// <para>
/// Phase-4a colour (spec 297, issue #2635): originally red on develop,
/// naming <c>LiveUpdatesBadge.tsx</c> (<c>border-accent-warning/40</c>,
/// <c>bg-accent-warning/15</c>), <c>TileAlignmentBadge.tsx</c>
/// (<c>bg-accent-warning/30</c>) and <c>LayoutGrid.tsx</c>'s *Overlay
/// unavailable* chip (<c>bg-accent-warning/30</c>); fixed since. The
/// <c>LayoutGrid.tsx</c> action button's own <c>bg-accent-active/20</c> was a
/// separate concern (affordance, not status) tolerated via the allowlist
/// below under #2694 until that issue shrank it to empty — see that fact's
/// own red-first note.
/// </para>
///
/// <para>
/// <b>Distinguishing a status label from an affordance for the solid case
/// (issue #2746):</b> this file's three status chips, and every other
/// legitimate status rendering, go through the shared <c>Badge</c> composite
/// (<c>apps/shared/src/ui/composites/Badge.tsx</c>'s <c>tone</c> prop), which
/// resolves to the <c>-subtle</c>/<c>-hover</c>/<c>-pressed</c>/<c>-border</c>
/// derived tokens — never a bare <c>bg-accent-active</c> or
/// <c>text-accent-fault</c> literal in <c>apps/kiosk-web/src</c> itself. So
/// the same scoping the translucent check already relied on (any raw triad
/// literal found in this tree, not routed through Badge, is by construction
/// an ad hoc local affordance/brand use, not a status rendering) extends
/// unchanged to the solid case: <see cref="TriadColourAsAffordance"/> matches
/// a bare triad utility with or without a call-site alpha suffix, but excludes
/// (via a trailing negative lookahead) anything immediately followed by
/// another hyphenated segment — exactly the derived-token names ADR-0146
/// reserves for status (<c>-subtle</c>, <c>-hover</c>, <c>-pressed</c>,
/// <c>-border</c>). A future legitimate exception still has the allowlist
/// escape valve below, reasoned and issue-tagged, same as before.
/// </para>
///
/// <para>
/// Red for real on widening (issue #2746, no synthetic fixture needed): the
/// solid case caught <c>App.tsx</c>'s sign-in buttons
/// (<c>bg-accent-active</c>, ×2) and <c>PickerPage.tsx</c>'s card hover
/// affordance (<c>hover:border-accent-active</c>, ×2) — both predated this
/// guard entirely (no prior check, translucent or solid, ever scanned them)
/// and both were exactly ADR-0146's "triad colour used for affordance": the
/// ADR introduces a separate, non-status accent hue for interactive
/// affordance precisely so the triad is never reached for by habit. Fixed in
/// the same change.
/// </para>
/// </summary>
public class WallStatusChipTests
{
    private const string KioskSrc = "apps/kiosk-web/src";

    /// <summary>
    /// Shrink-only (spec §3.2). Emptied by #2694 when the four wall action
    /// buttons moved to <c>bg-accent-subtle text-accent</c>.
    /// </summary>
    private static readonly (string RelativePath, string Match, string Reason)[] Allowlist = [];

    /// <summary>
    /// A triad colour (<c>active</c>/<c>warning</c>/<c>fault</c>) painted as a
    /// bare Tailwind utility, solid or with a call-site alpha suffix
    /// (<c>/NN</c>) — but never one of the derived semantic tokens
    /// (<c>-subtle</c>, <c>-hover</c>, <c>-pressed</c>, <c>-border</c>, …)
    /// those alpha/solid call sites were replaced by, which the trailing
    /// <c>(?![\w-])</c> excludes by refusing to match when another hyphenated
    /// segment immediately follows.
    /// </summary>
    private static readonly Regex TriadColourAsAffordance = new(
        @"(?<![\w-])(?:bg|border|text|ring|outline|fill|stroke)-accent-(?:active|warning|fault)(?:/\d+)?(?![\w-])",
        RegexOptions.Compiled);

    private static readonly Regex IssueReference = new(@"#\d+", RegexOptions.Compiled);

    /// <summary>
    /// Scans every file under <see cref="KioskSrc"/> for a triad colour used
    /// solid or translucent, failing on any match not covered by
    /// <see cref="Allowlist"/>.
    /// </summary>
    [Fact]
    public void No_triad_colour_used_as_affordance_on_the_wall()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            HashSet<string> allowedMatches = [
                .. Allowlist.Where(entry => entry.RelativePath == relativePath).Select(entry => entry.Match),
            ];

            string literalContent = TypeScriptSource.StringLiteralContent(
                File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in TriadColourAsAffordance.Matches(literalContent))
            {
                if (allowedMatches.Contains(match.Value))
                {
                    continue;
                }

                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a wall (kiosk-web) file uses a triad colour (solid or translucent) outside the allowlist — "
            + "ADR-0146: \"a triad colour (signal or text) used for affordance or brand\" is disqualified there:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}"))
            + Environment.NewLine
            + "cite the semantic -subtle tint through the shared Badge instead, or add a reasoned, "
            + "issue-tagged allowlist entry.");
    }

    /// <summary>
    /// <see cref="TriadColourAsAffordance"/> on its own, independent of any
    /// file scan: proves the matcher catches a bare solid triad utility (the
    /// gap #2746 filed) alongside the translucent form it already caught,
    /// without widening into the derived semantic tokens status rendering is
    /// built from.
    /// </summary>
    [Theory]
    [InlineData("bg-accent-active")]
    [InlineData("text-accent-fault")]
    [InlineData("border-accent-warning")]
    [InlineData("hover:border-accent-active")]
    [InlineData("bg-accent-active/20")]
    [InlineData("ring-accent-fault/40")]
    public void Triad_affordance_pattern_matches_solid_and_translucent_usage_alike(string candidate)
    {
        TriadColourAsAffordance.IsMatch(candidate).ShouldBeTrue();
    }

    /// <summary>
    /// The derived semantic tokens status rendering is built from (ADR-0146's
    /// own replacement for a call-site alpha modifier) must stay unmatched —
    /// otherwise widening this guard to solid usage would also flag the fix,
    /// not just the defect.
    /// </summary>
    [Theory]
    [InlineData("bg-accent-active-subtle")]
    [InlineData("bg-accent-warning-subtle")]
    [InlineData("bg-accent-fault-subtle")]
    [InlineData("bg-accent-fault-hover")]
    [InlineData("bg-accent-fault-pressed")]
    [InlineData("border-accent-fault-border")]
    [InlineData("bg-accent-subtle")]
    [InlineData("text-accent-active-text")]
    [InlineData("bg-accent-warning-on-video")]
    public void Triad_affordance_pattern_does_not_match_the_derived_semantic_tokens(string candidate)
    {
        TriadColourAsAffordance.IsMatch(candidate).ShouldBeFalse();
    }

    [Fact]
    public void Each_wall_allowlist_entry_still_matches()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> problems = [];

        foreach ((string relativePath, string match, string reason) in Allowlist)
        {
            IssueReference.IsMatch(reason).ShouldBeTrue(
                $"{relativePath}'s allowlist entry ({match}) must name the issue that owns it (e.g. #1234).");

            string path = Path.Combine(root.FullName, relativePath);
            if (!File.Exists(path))
            {
                problems.Add($"{relativePath} no longer exists — remove its allowlist entry.");
                continue;
            }

            string literalContent = TypeScriptSource.StringLiteralContent(File.ReadAllText(path));
            if (!literalContent.Contains(match, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{relativePath} no longer contains '{match}' — its allowlist entry no longer applies and "
                    + "must be removed (the list is shrink-only, never grows to cover something new).");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    private static IEnumerable<string> ScannedFiles(DirectoryInfo root)
    {
        string full = Path.Combine(root.FullName, KioskSrc);

        foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".tsx", StringComparison.Ordinal))
            {
                continue;
            }

            // Slash-normalised (repo memory: Path.GetRelativePath returns the
            // platform separator — a backslash-literal filter is green on
            // Windows and red on Linux CI).
            string relative = RepositorySource.RelativePath(root, file);

            if (relative.Contains(".test.", StringComparison.Ordinal) || relative.Contains(".spec.", StringComparison.Ordinal))
            {
                continue;
            }

            yield return relative;
        }
    }
}
