using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards US3 of spec 298 (issue #2693), ADR-0148: no file under
/// <c>apps/management-web/src</c> paints a triad colour with a call-site
/// alpha modifier (<c>bg-accent-fault/10</c>, <c>border-accent-fault/40</c>,
/// …) — every such box now cites the opaque <c>-subtle</c>/<c>-border</c>
/// tint stops through <c>FaultNotice</c>, <c>RetryBanner</c> or
/// <c>Badge</c>.
///
/// <para>
/// Shape copied from <see cref="WallStatusChipTests"/> (itself copied from
/// <see cref="SharedUiTokenUsageTests"/>'s string-literal scan via
/// <see cref="TypeScriptSource"/>), rooted at
/// <c>apps/management-web/src</c> instead of <c>apps/kiosk-web/src</c>.
/// Unlike that guard, this one carries <b>no allowlist</b>: spec 298 §1
/// found exactly 11 matches in 5 files, every one inside US1/US2's scope,
/// and after the fix there is nothing left to allow.
/// </para>
///
/// <para>
/// Phase-4a colour: red (spec §7). Red on `56f2cde8`, naming exactly:
/// <c>LayoutsPage.tsx</c> (×2), <c>OverlaysPage.tsx</c> (×2),
/// <c>RulesPage.tsx</c> (×3), <c>SystemVariablesPage.tsx</c> (×2),
/// <c>WallDetailPage.tsx</c> (×2).
/// </para>
///
/// <para>
/// The pattern also covers a tint stop's own alpha (<c>-subtle/50</c>,
/// <c>-border/60</c>), Tailwind's arbitrary-alpha syntax
/// (<c>/[0.1]</c>, <c>/[10%]</c>), and the other colour-bearing utilities
/// <see cref="SharedUiTokenUsageTests"/>'s broader sibling regex already
/// checks (<c>divide-</c>, <c>from-</c>/<c>via-</c>/<c>to-</c>,
/// <c>shadow-</c>, <c>decoration-</c>, <c>placeholder-</c>,
/// <c>caret-</c>) — not just the narrower set this guard's own fixture
/// happened to use.
/// </para>
/// </summary>
public class ConsoleTriadAlphaTests
{
    // Spec 316 widens this set rather than narrowing it (ADR-0144): the
    // cameras feature moved out of apps/management-web/src into its own
    // federated remote, apps/management-cameras/src. Without this, the
    // files move and this gate silently stops scanning them.
    private static readonly string[] ConsoleSrcTrees = ["apps/management-web/src", "apps/management-cameras/src"];

    private static readonly Regex TranslucentTriadColour = new(
        @"(?<![\w-])(?:bg|border|text|ring|outline|fill|stroke|divide|from|via|to|shadow|decoration|placeholder|caret)" +
        @"-accent-(?:active|warning|fault)(?:-[a-z]+)*/(?:\d+|\[)",
        RegexOptions.Compiled);

    /// <summary>
    /// S3 fix (spec 316 phase-6 review): <see cref="ScannedFiles"/> tolerates
    /// a missing tree (<c>continue</c>, not throw) so a rename doesn't crash
    /// the scan — but that same tolerance means a future rename of
    /// <c>apps/management-cameras</c> would silently narrow every fact in
    /// this class to scan nothing, rather than failing loudly. This fact is
    /// the loud failure: every entry in <see cref="ConsoleSrcTrees"/> must
    /// exist and contain at least one <c>.ts</c>/<c>.tsx</c> file.
    /// </summary>
    [Fact]
    public void Every_scanned_tree_exists_and_is_non_empty()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> problems = [];

        foreach (string tree in ConsoleSrcTrees)
        {
            string full = Path.Combine(root.FullName, tree);
            if (!Directory.Exists(full))
            {
                problems.Add($"{tree} does not exist.");
                continue;
            }

            bool hasAnyFile = Directory
                .EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Any(file => file.EndsWith(".ts", StringComparison.Ordinal) || file.EndsWith(".tsx", StringComparison.Ordinal));

            if (!hasAnyFile)
            {
                problems.Add($"{tree} exists but contains no .ts/.tsx file.");
            }
        }

        problems.ShouldBeEmpty(
            "a tree this class scans is missing or empty — every other fact in this class would silently "
            + "narrow to scanning nothing rather than failing:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(p => $"  {p}")));
    }

    /// <summary>
    /// A different set from the class doc's comment means the premise
    /// moved — stop (plan.md §4.4).
    /// </summary>
    [Fact]
    public void No_translucent_triad_colour_in_the_console()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> violations = [];

        foreach (string relativePath in ScannedFiles(root))
        {
            string literalContent = TypeScriptSource.StringLiteralContent(
                File.ReadAllText(Path.Combine(root.FullName, relativePath)));

            foreach (Match match in TranslucentTriadColour.Matches(literalContent))
            {
                violations.Add($"{relativePath}: {match.Value}");
            }
        }

        violations.ShouldBeEmpty(
            "a management-web file uses a call-site alpha modifier on a triad colour — ADR-0148: "
            + "components cite semantic roles, not call-site translucency:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}"))
            + Environment.NewLine
            + "cite the opaque -subtle/-border tint stops through FaultNotice, RetryBanner or Badge instead.");
    }

    private static IEnumerable<string> ScannedFiles(DirectoryInfo root)
    {
        foreach (string tree in ConsoleSrcTrees)
        {
            string full = Path.Combine(root.FullName, tree);
            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".tsx", StringComparison.Ordinal))
                {
                    continue;
                }

                // Slash-normalised: Path.GetRelativePath returns the platform
                // separator — a backslash-literal filter is green on Windows and
                // red on Linux CI.
                string relative = RepositorySource.RelativePath(root, file);

                if (relative.Contains(".test.", StringComparison.Ordinal) || relative.Contains(".spec.", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return relative;
            }
        }
    }
}
