using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the rule spec 048 paid for in an operator-visible defect: <b>a page is
/// not the list</b> (issue 1982, spec 065).
///
/// <para>
/// Three response types in <c>apps/shared/src/api</c> answer with as much as the
/// source could gather rather than with everything, and each carries a field
/// saying so — <c>count</c>, <c>complete</c>, <c>nextCursor</c>. A view that
/// renders the items and never reads that field presents a truncated answer as
/// the whole of it, and nothing on the screen says otherwise. The layout picker
/// did exactly that with a fifty-row page.
/// </para>
///
/// <para>
/// <b>The register lives here, not in a document.</b> A guard that reads its
/// expectations out of markdown proves the markdown was written, not that the
/// code obeys it; the two look identical from a green test and diverge the
/// first time someone edits the document to make the build pass.
/// </para>
///
/// <para>
/// <b>Declared limitations.</b> It sees a reference, not a use: a consumer that
/// names the field in a comment and ignores it passes. It is a source scan, not
/// a type check, so a bounded response reached through an intermediate helper in
/// a third file would be judged on the wrong file — no such indirection exists
/// today. And it polices <c>apps/</c> only; the one backend caller is recorded
/// in spec 065 as a follow-up.
/// </para>
/// </summary>
public class PaginatedConsumerTests
{
    private const string GuardSource = "tests/Architecture.Tests/PaginatedConsumerTests.cs";
    private const string ApiClients = "apps/shared/src/api";

    /// <summary>
    /// The bounded response types the register accounts for, in ordinal order.
    /// Kept beside the hook rows so that
    /// <see cref="The_register_names_every_bounded_response_the_api_clients_declare"/>
    /// fails when a fourth contract arrives.
    /// </summary>
    private static readonly string[] RegisteredResponseTypes =
        ["AuditPage", "CameraChoices", "CameraListPage"];

    private static readonly Regex InterfaceDeclaration = new(
        @"^export interface (?<name>\w+)[^{]*\{(?<body>.*?)^\}",
        RegexOptions.Multiline | RegexOptions.Singleline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// <b>Assertion 1 — every consumer reads the boundary.</b>
    ///
    /// <para>
    /// The register: each hook that answers with a bounded response, and the
    /// field a caller must consult to know whether it got everything.
    /// <c>CameraChoices</c> carries <c>count</c> as well, but <c>complete</c> is
    /// its boundary — <c>count</c> is deliberately a sentinel after a mid-walk
    /// page failure, so a consumer reading it alone would put a fabricated
    /// number in front of an operator.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("useListCamerasQuery", "count")]
    [InlineData("useListAllCameraChoicesQuery", "complete")]
    [InlineData("useSearchAuditQuery", "nextCursor")]
    [InlineData("useGetResourceTimelineQuery", "nextCursor")]
    public void Every_consumer_of_a_bounded_response_names_its_boundary_field(string hook, string boundaryField)
    {
        string[] silent = FrontendSources()
            .Where(file => file.Value.Contains(hook, StringComparison.Ordinal))
            .Where(file => !Mentions(file.Value, boundaryField))
            .Select(file => file.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        silent.ShouldBeEmpty(
            $"these files call {hook} and never name '{boundaryField}': {string.Join(", ", silent)}. "
            + $"A page is not the list — {hook} answers with as much as the source could gather, and a "
            + $"view that renders the items without reading '{boundaryField}' shows a truncated answer as "
            + "if it were everything, with nothing on screen to say so (spec 048). Read the field and tell "
            + "the operator what is missing, as CamerasPage and LayoutEditorDialog do.");
    }

    /// <summary>
    /// <b>Assertion 2 — the register is complete.</b>
    ///
    /// <para>
    /// Without this the guard is a snapshot with a longer shelf life: it polices
    /// the three contracts that existed when it was written and is blind to the
    /// fourth. It fails on a <em>new</em> bounded contract, which is exactly the
    /// moment a human should decide what its boundary field is.
    /// </para>
    /// </summary>
    [Fact]
    public void The_register_names_every_bounded_response_the_api_clients_declare()
    {
        string[] declared = BoundedResponseInterfaces();

        declared.ShouldBe(
            RegisteredResponseTypes,
            $"{ApiClients} declares [{string.Join(", ", declared)}] as bounded responses — a list field "
            + "beside a boundary field — but the register holds "
            + $"[{string.Join(", ", RegisteredResponseTypes)}]. A bounded response with no register row is "
            + "unguarded: its consumers may render a page as the list and nothing will fail. Add the new "
            + "type and the hook that produces it, and decide which field a caller must read.");
    }

    /// <summary>
    /// <b>Assertion 3 — the gate has no soft edge.</b>
    ///
    /// <para>
    /// FR-005 makes a necessary departure a <em>blocked outcome</em> a human
    /// accepts in writing, not a line added here, and the failure ADR-0144 names
    /// is an agent reaching green by weakening a gate. Comment lines and
    /// attribute lines are not scanned, so this prose and the rows above are
    /// free to say what they mean.
    /// </para>
    ///
    /// <para>
    /// If it proves awkward in practice it should be removed by a human with a
    /// stated reason, not quietly.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("allowlist")]
    [InlineData("allowList")]
    [InlineData("whitelist")]
    [InlineData("skiplist")]
    [InlineData("skipList")]
    [InlineData("exempt")]
    [InlineData("waiver")]
    [InlineData("waived")]
    [InlineData("knownViolation")]
    [InlineData("suppress")]
    public void The_guard_offers_no_way_to_excuse_a_consumer(string mechanism)
    {
        string[] offenders = ExecutableLines(ReadRepositoryFile(GuardSource))
            .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the guard's own code names '{mechanism}': {string.Join(" | ", offenders)}. That reads as a "
            + "way to excuse a consumer from the rule, and a rule with a soft edge is a review convention "
            + "wearing a build failure's clothes. A departure is a blocked outcome a human accepts in "
            + "writing (FR-005); ADR-0144 forbids the lane from reaching green by weakening a gate.");
    }

    /// <summary>
    /// Whether the source names the field as a word. Substring matching would
    /// let <c>accountId</c> stand in for <c>count</c>.
    /// </summary>
    private static bool Mentions(string source, string field) =>
        Regex.IsMatch(source, $@"\b{field}\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// Exported interfaces in the API clients that carry a list field beside a
    /// boundary field — the offset shape (<c>count</c> with <c>offset</c> /
    /// <c>limit</c>), the gathered shape (<c>count</c> with <c>complete</c>) or
    /// the cursor shape (<c>nextCursor</c>). A bare <c>count</c> does not trip
    /// it.
    /// </summary>
    private static string[] BoundedResponseInterfaces()
    {
        DirectoryInfo root = RepositoryRoot();
        string directory = Path.Combine(root.FullName, ApiClients);

        return Directory.EnumerateFiles(directory, "*.api.ts", SearchOption.TopDirectoryOnly)
            .SelectMany(file => InterfaceDeclaration.Matches(File.ReadAllText(file)).AsEnumerable())
            .Where(declaration => IsBounded(declaration.Groups["body"].Value))
            .Select(declaration => declaration.Groups["name"].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsBounded(string body) =>
        body.Contains("[]", StringComparison.Ordinal)
        && (Mentions(body, "nextCursor")
            || (Mentions(body, "count")
                && (Mentions(body, "offset") || Mentions(body, "limit") || Mentions(body, "complete"))));

    /// <summary>
    /// Every TypeScript source under <c>apps/*/src</c> that could consume a
    /// bounded response, keyed by repository-relative path. Test files and the
    /// producing API clients are left out: the producer names both the hook and
    /// the field by construction, and a test asserting on a truncated fixture is
    /// doing its job.
    /// </summary>
    private static Dictionary<string, string> FrontendSources()
    {
        DirectoryInfo root = RepositoryRoot();

        return Directory.EnumerateDirectories(Path.Combine(root.FullName, "apps"))
            .Select(app => Path.Combine(app, "src"))
            .Where(Directory.Exists)
            .SelectMany(src => Directory.EnumerateFiles(src, "*.ts*", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .Where(IsConsumerCandidate)
            .ToDictionary(
                path => path,
                path => File.ReadAllText(Path.Combine(root.FullName, path)),
                StringComparer.Ordinal);
    }

    private static bool IsConsumerCandidate(string path) =>
        (path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
        && !path.EndsWith(".d.ts", StringComparison.Ordinal)
        && !path.EndsWith(".test.ts", StringComparison.Ordinal)
        && !path.EndsWith(".test.tsx", StringComparison.Ordinal)
        && !(path.StartsWith($"{ApiClients}/", StringComparison.Ordinal)
            && path.EndsWith(".api.ts", StringComparison.Ordinal));

    /// <summary>
    /// Forward slashes throughout, and every path comparison in this file is
    /// written against them. <c>Path.GetRelativePath</c> returns the
    /// <em>platform</em> separator, so a filter written with a backslash literal
    /// is green on a Windows developer machine and red on Linux CI — the worst
    /// direction for a guard to break, because it passes exactly where nobody
    /// looks.
    /// </summary>
    private static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace('\\', '/');

    /// <summary>
    /// Lines that are neither commentary nor attribute metadata — the ones that
    /// could actually carry a mechanism.
    /// </summary>
    private static IEnumerable<string> ExecutableLines(string source) =>
        source.Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                && !line.StartsWith('['));

    private static string ReadRepositoryFile(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllText(path);
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
}
