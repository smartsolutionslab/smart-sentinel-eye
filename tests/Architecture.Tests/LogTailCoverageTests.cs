using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the integration fixture's log-tail coverage (issue #2053).
///
/// <para>
/// <c>AspireFixture.RecentLogs</c> serves a resource only if that resource is
/// being tailed, and the tailed set is a hand-maintained literal. A request for
/// anything else returns a self-describing placeholder — good writing, and
/// precisely why the gap survives: on the CI failure where the log was needed,
/// the assertion message reads like output rather than like an omission.
/// </para>
///
/// <para>
/// <b>It checks one direction only: <c>requested ⊆ tailed</c>.</b> Two gaps
/// follow, and neither is covered anywhere else in this project. The reverse
/// containment — <c>tailed ⊆ app-model names</c> — is unchecked, so a misspelled
/// entry in <c>TailedResources</c> spins the fixture's resolve loop forever,
/// records no failure, and yields exactly the placeholder this guard exists to
/// remove; only <c>LogTailDeliversIntegrationTests</c> test B catches that, and
/// it needs Docker. And <c>tailed ⊆ requested</c> is unchecked too, so a name
/// nobody asks about keeps its subscription — an unpriced cost while assumption
/// A1 (the marginal cost of a tail) is still unmeasured.
/// </para>
///
/// <para>
/// Reads source from disk rather than referencing <c>Integration.Tests</c>.
/// <c>TailedResources</c> is <c>private static</c>, so reflection cannot see it
/// without widening its accessibility, and a project reference would drag the
/// Aspire hosting and DCP dependency graph into a project that today runs in
/// seconds with no Docker. <c>GuardBanWiringTests</c> reads repository files for
/// the same reason.
/// </para>
/// </summary>
public class LogTailCoverageTests
{
    private const string Fixture = "tests/Integration.Tests/Fixtures/AspireFixture.cs";
    private const string ScannedTree = "tests/Integration.Tests";

    /// <summary>
    /// A request whose argument can be read from source. A variable argument is
    /// skipped rather than failed: it cannot be checked by reading source, and
    /// failing it would force a rewrite of correct code.
    /// </summary>
    private static readonly Regex LiteralRequest = new(
        @"RecentLogs\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex TailedList = new(
        @"TailedResources\s*=\s*\[(?<names>[^\]]*)\]",
        RegexOptions.Compiled);

    [Fact]
    public void Every_resource_a_test_asks_for_logs_from_is_tailed()
    {
        HashSet<string> tailed = TailedResources();
        Request[] requests = LiteralRequests();

        requests.Length.ShouldBeGreaterThan(
            0,
            $"no RecentLogs call sites were found under {ScannedTree} — the scan is broken, not the code. "
            + "A source-scanning guard that matches nothing passes, and a passing guard that checks "
            + "nothing is indistinguishable from one that holds.");

        Request[] violations = requests.Where(request => !tailed.Contains(request.Resource)).ToArray();

        violations.ShouldBeEmpty(Explain(violations, tailed, requests.Length));
    }

    /// <summary>
    /// The parse is the other half of the comparison, and it fails silently: a
    /// reformatted declaration yields an empty set, which would report every
    /// call site as a violation rather than none — loud, but for the wrong
    /// reason. This says which of the two broke.
    /// </summary>
    [Fact]
    public void The_tailed_resource_list_is_read_from_the_fixture()
    {
        TailedResources().ShouldNotBeEmpty(
            $"AspireFixture.TailedResources parsed to nothing out of {Fixture}. The declaration moved or "
            + "was reformatted past this guard's reach; fix the parse before trusting either verdict.");
    }

    private static string Explain(Request[] violations, HashSet<string> tailed, int total)
    {
        List<string> message =
        [
            $"{violations.Length} of {total} RecentLogs call sites name a resource AspireFixture does not "
            + "tail, so each one reports \"(not tailed — …)\" where the service's log should be:",
        ];

        foreach (IGrouping<string, Request> group in violations
            .GroupBy(violation => violation.Resource, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            message.Add(string.Empty);
            message.AddRange(group
                .OrderBy(request => request.Path, StringComparer.Ordinal)
                .ThenBy(request => request.Line)
                .Select(request => $"  {request.Path}:{request.Line} asks for '{request.Resource}'"));
        }

        message.Add(string.Empty);
        message.Add($"Tailed today: {string.Join(", ", tailed.Order(StringComparer.Ordinal))}.");
        message.Add(
            $"Add the missing name(s) to AspireFixture.TailedResources ({Fixture}) — the same place the "
            + "placeholder itself points at.");

        return string.Join(Environment.NewLine, message);
    }

    private static Request[] LiteralRequests()
    {
        DirectoryInfo root = RepositoryRoot();

        return Directory
            .EnumerateFiles(Path.Combine(root.FullName, ScannedTree), "*.cs", SearchOption.AllDirectories)
            .Select(file => Relative(root, file))
            .Where(IsSource)
            .SelectMany(relative => RequestsIn(root, relative))
            .ToArray();
    }

    private static IEnumerable<Request> RequestsIn(DirectoryInfo root, string relative)
    {
        string[] lines = File.ReadAllLines(Path.Combine(root.FullName, relative));

        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match match in LiteralRequest.Matches(lines[i]))
            {
                yield return new Request(relative, i + 1, match.Groups[1].Value);
            }
        }
    }

    private static HashSet<string> TailedResources()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, Fixture));

        Match declaration = TailedList.Match(source);
        if (!declaration.Success)
        {
            return [];
        }

        return declaration.Groups["names"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim('"'))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reported with <c>/</c> throughout. <see cref="Path.GetRelativePath"/>
    /// returns the platform separator, so a backslash in an expected string is
    /// green on Windows and red on Linux CI — this repository has been bitten by
    /// exactly that.
    /// </summary>
    private static string Relative(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsSource(string relative) =>
        !relative.Contains("/obj/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal);

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

    private sealed record Request(string Path, int Line, string Resource);
}
