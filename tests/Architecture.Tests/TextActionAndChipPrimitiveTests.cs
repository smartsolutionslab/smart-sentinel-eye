using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards spec 427 (issue #2668): an underlined text action is the design-system
/// <c>LinkButton</c>, not a raw <c>&lt;button className="underline"&gt;</c> that re-spells the
/// look and misses the focus ring. A hit outside <c>LinkButton.tsx</c> is a violation.
///
/// <para>
/// Source-scanning over comment-stripped, non-test <c>apps/*/src/**/*.tsx</c> (line numbers
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
/// </summary>
public class TextActionAndChipPrimitiveTests
{
    private const string LinkButtonPath = "apps/shared/src/ui/primitives/LinkButton.tsx";

    private static readonly Regex UnderlineUtility = new(
        @"(?<![\w-])underline(?![\w-])",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

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
            + "(and gives it the design-system focus ring). Use <LinkButton> (import from "
            + "'@smart-sentinel-eye/shared/ui/primitives/LinkButton', never the ui barrel): "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    [Fact]
    public void Each_allowlist_entry_still_applies()
    {
        var root = RepositorySource.Root();
        List<string> problems = [];

        if (!File.Exists(Path.Combine(root.FullName, LinkButtonPath)))
        {
            problems.Add($"{LinkButtonPath} no longer exists — the one file allowed to spell `underline` moved.");
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
                $"no non-test .tsx file was found under {sourceFolder}; every other fact here would pass vacuously.");
        }
    }

    private static List<string> ScannedSourceFiles(DirectoryInfo root) =>
        [
            .. Directory
                .EnumerateFiles(Path.Combine(root.FullName, "apps"), "*.tsx", SearchOption.AllDirectories)
                .Select(file => RepositorySource.RelativePath(root, file))
                .Where(relative =>
                    Regex.IsMatch(relative, @"^apps/[^/]+/src/", RegexOptions.None, TimeSpan.FromSeconds(5))
                    && !relative.Contains("/node_modules/", StringComparison.Ordinal)
                    && !relative.Contains(".test.", StringComparison.Ordinal)
                    && !relative.Contains(".spec.", StringComparison.Ordinal)
                    && !relative.Contains("/test/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

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
