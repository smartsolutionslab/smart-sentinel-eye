using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US2 of spec 297 (issue #2635), ADR-0146: the wall's three status
/// chips (<c>LiveUpdatesBadge</c>, <c>TileAlignmentBadge</c>, the *Overlay
/// unavailable* chip in <c>LayoutGrid.tsx</c>) render an opaque fill, not a
/// translucent call-site alpha modifier over live video.
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
/// </summary>
public class WallStatusChipTests
{
    private const string KioskSrc = "apps/kiosk-web/src";

    /// <summary>
    /// Shrink-only (spec §3.2). Emptied by #2694 when the four wall action
    /// buttons moved to <c>bg-accent-subtle text-accent</c>.
    /// </summary>
    private static readonly (string RelativePath, string Match, string Reason)[] Allowlist = [];

    private static readonly Regex TranslucentTriadColour = new(
        @"(?<![\w-])(?:bg|border|text|ring|outline|fill|stroke)-accent-(?:active|warning|fault)/\d+",
        RegexOptions.Compiled);

    private static readonly Regex IssueReference = new(@"#\d+", RegexOptions.Compiled);

    /// <summary>
    /// Scans every file under <see cref="KioskSrc"/> for the translucent
    /// triad colour pattern, failing on any match not covered by
    /// <see cref="Allowlist"/>.
    /// </summary>
    [Fact]
    public void No_translucent_triad_colour_on_the_wall()
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

            foreach (Match match in TranslucentTriadColour.Matches(literalContent))
            {
                if (allowedMatches.Contains(match.Value))
                {
                    continue;
                }

                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a wall (kiosk-web) file uses a translucent call-site alpha modifier on a triad colour, outside "
            + "the allowlist — ADR-0146: \"any translucency over live video\" is disqualified there:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}"))
            + Environment.NewLine
            + "cite the semantic -subtle tint through the shared Badge instead, or add a reasoned, "
            + "issue-tagged allowlist entry.");
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
