using System.Text;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards spec 425 (issue #2667): the kiosk's action buttons are the design-system
/// <c>Button</c> at <c>size="touch"</c>, not raw <c>&lt;button&gt;</c> elements that each
/// re-spell a smaller, hand-padded target. A raw button in <c>apps/kiosk-web/src</c> is a
/// violation unless it sits on the shrink-only allowlist below, with a reason.
///
/// <para>
/// Source-scanning, comment-stripped (line numbers preserved so a violation names a real
/// <c>file:line</c>), paths forward-slashed through <see cref="RepositorySource.RelativePath"/>
/// so the guard reads the same on a Windows machine and on Linux CI.
/// </para>
///
/// <para>
/// <b>Phase 4a (ADR-0139):</b> <see cref="A_kiosk_action_button_is_the_shared_Button"/> is RED on
/// <c>develop</c> naming the seven migrating sites (S1-S7 in spec 425 §1.1). The honesty and
/// non-empty-scan facts are green in advance and are proved by a planted counterfactual.
/// </para>
/// </summary>
public class KioskButtonPrimitiveTests
{
    private const string KioskSrc = "apps/kiosk-web/src";

    private static readonly Regex RawButtonOpening = new(@"<button\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// The raw buttons that stay raw. Each <c>Match</c> is a distinctive substring of the
    /// element's opening tag, so the entry stops applying when the element goes. Shrink-only.
    /// </summary>
    private static readonly (string RelativePath, string Match, string Reason)[] RawButtonAllowlist =
    [
        (
            "apps/kiosk-web/src/features/picker/PickerPage.tsx",
            "onOpen(layout.layoutIdentifier)",
            "spec 425 §1.1 S8: a full-width, left-aligned card with a heading and meta line (p-6), "
            + "not a button shape; already well over 48 px."),
        (
            "apps/kiosk-web/src/features/picker/PickerPage.tsx",
            "onOpenWall(wall.wallIdentifier)",
            "spec 425 §1.1 S9: the wall card, same reason as the layout card."),
    ];

    [Fact]
    public void A_kiosk_action_button_is_the_shared_Button()
    {
        var root = RepositorySource.Root();
        List<string> violations = [];

        foreach (var relativePath in ScannedKioskFiles(root, KioskSrc))
        {
            foreach (var button in RawButtons(root, relativePath))
            {
                var allowlisted = RawButtonAllowlist.Any(entry =>
                    entry.RelativePath == relativePath
                    && button.OpeningTag.Contains(entry.Match, StringComparison.Ordinal));

                if (!allowlisted)
                {
                    violations.Add($"{relativePath}:{button.Line}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "a raw <button> in the kiosk re-spells a target the shared Button already sizes. Use "
            + "<Button size=\"touch\"> (import from '@smart-sentinel-eye/shared/ui/primitives/Button', never "
            + "the ui barrel), or add a reasoned allowlist entry if it is a card, not a button shape: "
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    [Fact]
    public void Each_allowlist_entry_still_applies()
    {
        var root = RepositorySource.Root();
        List<string> problems = [];

        foreach ((var relativePath, var match, var reason) in RawButtonAllowlist)
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

            var applies = RawButtons(root, relativePath)
                .Any(button => button.OpeningTag.Contains(match, StringComparison.Ordinal));

            if (!applies)
            {
                problems.Add(
                    $"{relativePath} has no raw <button> whose opening tag contains '{match}' — its allowlist "
                    + "entry no longer applies and must be removed (the list is shrink-only).");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// A guard that scans nothing passes everything. If the kiosk tree moves or the filter
    /// over-excludes, this is the loud failure.
    /// </summary>
    [Fact]
    public void The_scan_reads_kiosk_source()
    {
        var root = RepositorySource.Root();

        ScannedKioskFiles(root, KioskSrc).ShouldNotBeEmpty(
            $"no non-test .tsx file was found under {KioskSrc}; every other fact here would pass vacuously.");
    }

    private static List<string> ScannedKioskFiles(DirectoryInfo root, string sourceFolder) =>
        [
            .. Directory
                .EnumerateFiles(Path.Combine(root.FullName, sourceFolder), "*.tsx", SearchOption.AllDirectories)
                .Select(file => RepositorySource.RelativePath(root, file))
                .Where(relative =>
                    !relative.Contains(".test.", StringComparison.Ordinal)
                    && !relative.Contains(".spec.", StringComparison.Ordinal)
                    && !relative.Contains("/test/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

    private static List<(int Line, string OpeningTag)> RawButtons(DirectoryInfo root, string relativePath)
    {
        var source = StripCommentsKeepingLines(File.ReadAllText(Path.Combine(root.FullName, relativePath)));
        List<(int Line, string OpeningTag)> buttons = [];

        foreach (Match match in RawButtonOpening.Matches(source))
        {
            var line = source.AsSpan(0, match.Index).Count('\n') + 1;
            buttons.Add((line, OpeningTagFrom(source, match.Index)));
        }

        return buttons;
    }

    /// <summary>
    /// From <c>&lt;button</c> to the <c>&gt;</c> that closes the tag, skipping any <c>&gt;</c>
    /// inside a <c>{...}</c> expression (an arrow function's <c>=&gt;</c>).
    /// </summary>
    private static string OpeningTagFrom(string source, int start)
    {
        var depth = 0;

        for (var index = start; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case '>' when depth == 0:
                    return source[start..(index + 1)];
            }
        }

        return source[start..];
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
