using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards <c>.github/dependabot.yml</c> (issue #2813) — the config this repo
/// relies on to open dependency-update PRs automatically instead of drift
/// being found manually. Read from disk as text (ADR-0103's reasoning for
/// <see cref="DockerfileUpstreamPinTests"/> applies the same way here: build
/// configuration leaves no trace in IL).
///
/// <para>
/// Parsing is line-based, not a full YAML parser — no YAML library is
/// referenced anywhere in this solution today, and this file's shape (a flat
/// list of <c>updates:</c> entries, each a short block of scalar keys) does
/// not need one.
/// </para>
/// </summary>
public class DependabotConfigTests
{
    private const string ConfigPath = ".github/dependabot.yml";

    /// <summary>Every ecosystem this repository actually has a manifest for (issue #2813's own list).</summary>
    private static readonly string[] ExpectedEcosystems = ["nuget", "npm", "github-actions", "docker"];

    private static readonly Regex EcosystemLine = new(
        @"^\s*-\s*package-ecosystem:\s*[""']?(?<ecosystem>[\w-]+)[""']?\s*$",
        RegexOptions.Compiled);

    [Fact]
    public void The_config_declares_schema_version_2()
    {
        string[] lines = ReadLines();

        lines.ShouldContain(line => Regex.IsMatch(line, @"^\s*version:\s*2\s*$"));
    }

    [Fact]
    public void Every_ecosystem_this_repository_has_is_declared_exactly_once()
    {
        EcosystemBlock[] blocks = EcosystemBlocks();

        string[] declared = [.. blocks.Select(block => block.Ecosystem).OrderBy(e => e, StringComparer.Ordinal)];
        string[] expected = [.. ExpectedEcosystems.OrderBy(e => e, StringComparer.Ordinal)];

        declared.ShouldBe(expected);
    }

    [Fact]
    public void Every_entry_targets_develop_explicitly_not_the_dependabot_default()
    {
        EcosystemBlock[] blocks = EcosystemBlocks();

        EcosystemBlock[] missingTarget = [.. blocks.Where(block => !block.Lines.Any(TargetsDevelop))];

        missingTarget.ShouldBeEmpty(
            $"these ecosystems don't set target-branch: develop explicitly: "
            + string.Join(", ", missingTarget.Select(block => block.Ecosystem)));
    }

    [Fact]
    public void Every_entry_carries_the_dependencies_label()
    {
        EcosystemBlock[] blocks = EcosystemBlocks();

        EcosystemBlock[] unlabelled = [.. blocks.Where(block => !block.Lines.Any(HasDependenciesLabel))];

        unlabelled.ShouldBeEmpty(
            $"these ecosystems don't carry the 'dependencies' label: "
            + string.Join(", ", unlabelled.Select(block => block.Ecosystem)));
    }

    [Fact]
    public void Every_entry_polls_weekly()
    {
        EcosystemBlock[] blocks = EcosystemBlocks();

        EcosystemBlock[] offCadence = [.. blocks.Where(block => !block.Lines.Any(IsWeeklyInterval))];

        offCadence.ShouldBeEmpty(
            $"these ecosystems don't set interval: weekly: "
            + string.Join(", ", offCadence.Select(block => block.Ecosystem)));
    }

    /// <summary>
    /// Minor/patch bumps are grouped per ecosystem (fewer, larger PRs); a major
    /// bump is not — issue #2813 is explicit that a major bump often needs code
    /// changes and should surface as its own PR, not be folded into a group.
    /// </summary>
    [Fact]
    public void Minor_and_patch_updates_are_grouped_but_major_updates_are_not()
    {
        EcosystemBlock[] blocks = EcosystemBlocks();

        EcosystemBlock[] notGrouped = [.. blocks.Where(block => !block.Lines.Any(line => line.Contains("groups:")))];
        notGrouped.ShouldBeEmpty(
            $"these ecosystems have no groups: block: " + string.Join(", ", notGrouped.Select(b => b.Ecosystem)));

        EcosystemBlock[] groupsMajor =
        [
            .. blocks.Where(block => block.Lines.Any(line => Regex.IsMatch(line, @"-\s*[""']?major[""']?\s*$"))),
        ];
        groupsMajor.ShouldBeEmpty(
            $"these ecosystems group major updates, which should surface as their own PR: "
            + string.Join(", ", groupsMajor.Select(b => b.Ecosystem)));
    }

    private static bool TargetsDevelop(string line) =>
        Regex.IsMatch(line, @"^\s*target-branch:\s*[""']?develop[""']?\s*$");

    private static bool HasDependenciesLabel(string line) =>
        line.Contains("dependencies", StringComparison.Ordinal) && line.TrimStart().StartsWith('-');

    private static bool IsWeeklyInterval(string line) =>
        Regex.IsMatch(line, @"^\s*interval:\s*[""']?weekly[""']?\s*$");

    /// <summary>
    /// Splits the file into one block per <c>- package-ecosystem: &lt;x&gt;</c>
    /// entry, each running until the next such line or end of file — mirrors
    /// how <c>DockerfileUpstreamPinTests</c> folds logical units out of a flat
    /// line list rather than building a tree.
    /// </summary>
    private static EcosystemBlock[] EcosystemBlocks()
    {
        string[] lines = ReadLines();
        List<EcosystemBlock> blocks = [];

        int index = 0;
        while (index < lines.Length)
        {
            Match match = EcosystemLine.Match(lines[index]);
            if (!match.Success)
            {
                index++;
                continue;
            }

            string ecosystem = match.Groups["ecosystem"].Value;
            List<string> blockLines = [lines[index]];
            index++;

            while (index < lines.Length && !EcosystemLine.IsMatch(lines[index]))
            {
                blockLines.Add(lines[index]);
                index++;
            }

            blocks.Add(new EcosystemBlock(ecosystem, [.. blockLines]));
        }

        return [.. blocks];
    }

    private static string[] ReadLines()
    {
        string path = Path.Combine(RepositoryRoot().FullName, ConfigPath);
        File.Exists(path).ShouldBeTrue(
            $"expected {ConfigPath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllLines(path);
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

    private sealed record EcosystemBlock(string Ecosystem, string[] Lines);
}
