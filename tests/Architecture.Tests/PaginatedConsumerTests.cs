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
/// <b>One register, joined at both ends.</b> A row is a triple — the bounded
/// response, the hook that produces it, the field a caller must read — and the
/// same rows drive the consumer sweep and the completeness check. The
/// response-to-hook half is checked against the <c>build.query</c> declarations
/// that actually produce those responses, so adding a type name alone cannot
/// restore green: the smallest edit that satisfies both halves is the one that
/// names the producing hook and decides what the new contract's boundary field
/// is. Held as two unjoined lists it guaranteed only that someone had been
/// told, which is the review convention this guard exists to replace.
/// </para>
///
/// <para>
/// <b>Declared limitations, in both directions.</b>
/// </para>
///
/// <para>
/// <i>False negatives.</i> It sees a reference, not a use: a consumer that names
/// the field in a comment and ignores it passes. It is a source scan, not a type
/// check, so a bounded response reached through an intermediate helper in a
/// third file would be judged on the wrong file — no such indirection exists
/// today. And it polices <c>apps/</c> only; the one backend caller is recorded
/// in spec 065 as a follow-up.
/// </para>
///
/// <para>
/// <i>False positives.</i> A component that forwards the whole response to a
/// child — <c>&lt;CameraTable page={data} /&gt;</c> — reads the boundary in the
/// child and will still be asked to name it here; so will a call made only to
/// warm the cache, which renders nothing to qualify. Neither shape exists today.
/// Recorded so that the first occurrence of either reads as a known limit to be
/// discussed with a human, not as a broken guard to be worked around.
/// </para>
/// </summary>
public class PaginatedConsumerTests
{
    private const string GuardSource = "tests/Architecture.Tests/PaginatedConsumerTests.cs";
    private const string ApiClients = "apps/shared/src/api";

    /// <summary>
    /// What the completeness check reports for a bounded response that no
    /// <c>build.query</c> produces. It can never equal a register row — every
    /// hook name begins <c>use</c> — so such a type arrives red rather than
    /// quietly outside the sweep.
    /// </summary>
    private const string Unproduced = "(nothing declares it)";

    /// <summary>
    /// The corpus was 84 files across three apps on 2026-09-04. The bound is
    /// less than half of that: ordinary churn cannot reach it, and a restructure
    /// that moves an app's sources out of <c>src/</c> falls straight through it.
    /// </summary>
    private const int SmallestPlausibleCorpus = 40;

    /// <summary>
    /// The register. One row per (bounded response, producing hook, boundary
    /// field), in ordinal order.
    ///
    /// <para>
    /// <c>CameraChoices</c> carries <c>count</c> as well, but <c>complete</c> is
    /// its boundary — <c>count</c> is deliberately a sentinel after a mid-walk
    /// page failure, so a consumer reading it alone would put a fabricated
    /// number in front of an operator.
    /// </para>
    /// </summary>
    private static readonly BoundedResponse[] Register =
    [
        new("AuditPage", "useGetResourceTimelineQuery", "nextCursor"),
        new("AuditPage", "useSearchAuditQuery", "nextCursor"),
        new("CameraChoices", "useListAllCameraChoicesQuery", "complete"),
        new("CameraListPage", "useListCamerasQuery", "count"),
    ];

    /// <summary>
    /// An exported object shape — <c>export interface X {</c> or
    /// <c>export type X = {</c>, the second having precedent at
    /// <c>rules.api.ts:84</c>. The head is confined to its own line and the body
    /// admits one level of nesting, so a declaration can neither borrow the next
    /// one's brace nor run away to the end of the file.
    /// </summary>
    private static readonly Regex ExportedShape = new(
        @"^export (?:interface|type) (?<name>\w+)[^{\r\n]*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// An RTK Query read endpoint and the type it answers with. The generated
    /// hook is <c>use</c> + the endpoint name capitalised + <c>Query</c>, which
    /// is how a response type and its hook are joined without keeping a second
    /// list by hand.
    /// </summary>
    private static readonly Regex QueryDeclaration = new(
        @"(?<endpoint>\w+): build\.query<\s*(?<response>\w+(?:\[\])?)\s*,",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The register's hook and boundary field, for the consumer sweep.
    /// </summary>
    public static TheoryData<string, string> RegisteredHooks()
    {
        TheoryData<string, string> data = [];
        foreach (BoundedResponse row in Register)
        {
            data.Add(row.Hook, row.BoundaryField);
        }

        return data;
    }

    /// <summary>
    /// <b>Assertion 1 — every consumer reads the boundary.</b>
    ///
    /// <para>
    /// Driven by the register, so a row cannot exist without being swept and a
    /// hook cannot be swept without a row.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RegisteredHooks))]
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
    /// <b>Assertion 2 — the register is complete, hook and all.</b>
    ///
    /// <para>
    /// Every bounded response the API clients declare, paired with every
    /// <c>build.query</c> that answers with it, must appear in the register.
    /// Without the pairing this compared two unjoined lists, and a new contract
    /// could be silenced by adding its type name alone: assertion 1 would gain
    /// no row, and every consumer of its hook would stay unswept.
    /// </para>
    /// </summary>
    [Fact]
    public void The_register_pairs_every_bounded_response_with_the_hook_that_produces_it()
    {
        string[] declared = DeclaredBoundedPairs();
        string[] registered = Register
            .Select(row => $"{row.ResponseType} -> {row.Hook}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        declared.ShouldBe(
            registered,
            $"{ApiClients} declares [{string.Join(", ", declared)}] — bounded responses, each paired with "
            + "the read endpoint that answers with it — but the register holds "
            + $"[{string.Join(", ", registered)}]. A pair with no register row is unguarded: its consumers "
            + "may render a page as the list and nothing will fail. Add the row, and decide which field a "
            + $"caller must read. A pair reading '{Unproduced}' is a bounded response no read endpoint "
            + "produces — say how it reaches a screen before registering it.");
    }

    /// <summary>
    /// <b>Assertion 2b — the sweep has something to sweep.</b>
    ///
    /// <para>
    /// <see cref="FrontendSources"/> globs <c>apps/*/src</c> and keeps whatever
    /// it finds, nothing included. An app restructured to hold its sources
    /// elsewhere leaves the corpus silently, and every row of assertion 1 then
    /// passes vacuously and permanently — the worst failure available to a
    /// guard, because it is indistinguishable from compliance.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_app_puts_its_sources_where_the_consumer_sweep_looks()
    {
        string[] unswept = AppDirectories()
            .Where(app => !Directory.Exists(Path.Combine(app, "src")))
            .Select(app => new DirectoryInfo(app).Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unswept.ShouldBeEmpty(
            $"these apps have a package.json but no src/ directory: {string.Join(", ", unswept)}. The "
            + "consumer sweep globs apps/*/src, so their files are not read at all and every row of "
            + "assertion 1 passes without looking at them. Teach the sweep the new layout rather than "
            + "letting the guard go quiet.");
    }

    /// <summary>
    /// <b>Assertion 2c — the corpus is not a rounding error.</b>
    ///
    /// <para>
    /// The companion to 2b: three roots can resolve and still yield almost
    /// nothing if the file filter stops matching. One vacuous row is already
    /// live and documented — <c>useGetResourceTimelineQuery</c> has no consumer
    /// today — and a documented vacuity does not detect an undocumented one.
    /// </para>
    /// </summary>
    [Fact]
    public void The_consumer_sweep_reads_a_corpus_large_enough_to_hold_a_violation()
    {
        int corpus = FrontendSources().Count;

        corpus.ShouldBeGreaterThanOrEqualTo(
            SmallestPlausibleCorpus,
            $"the consumer sweep found {corpus} files under apps/*/src, fewer than the "
            + $"{SmallestPlausibleCorpus} a real front end has. Assertion 1 is then green because it read "
            + "almost nothing, not because the consumers are correct. Something moved, or the file filter "
            + "stopped matching — fix the sweep before trusting any row above it.");
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
    /// It polices a vocabulary, not a mechanism. It catches the obvious move by
    /// its spelling; an author who names the same thing something else walks
    /// past it. Three lines is a fair price for making the obvious move loud,
    /// which is the move an agent under pressure to reach green makes — but it
    /// is not a proof that no soft edge can be added. If it proves awkward in
    /// practice it should be removed by a human with a stated reason, not
    /// quietly.
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
    /// Every bounded response the API clients declare, paired with each
    /// producing endpoint's generated hook, as <c>Type -&gt; useThingQuery</c>
    /// in ordinal order. A bounded response no endpoint produces is paired with
    /// <see cref="Unproduced"/> rather than dropped.
    /// </summary>
    private static string[] DeclaredBoundedPairs()
    {
        string[] clients = ApiClientSources();

        ILookup<string, string> producers = clients
            .SelectMany(text => QueryDeclaration.Matches(text).AsEnumerable())
            .ToLookup(
                match => match.Groups["response"].Value,
                match => HookFor(match.Groups["endpoint"].Value),
                StringComparer.Ordinal);

        return BoundedResponses(clients)
            .SelectMany(type => producers[type].DefaultIfEmpty(Unproduced).Select(hook => $"{type} -> {hook}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// RTK Query generates <c>use</c> + the capitalised endpoint name +
    /// <c>Query</c>. If that convention ever changes, the derived hook stops
    /// matching the register and assertion 2 says so.
    /// </summary>
    private static string HookFor(string endpoint) =>
        $"use{char.ToUpperInvariant(endpoint[0])}{endpoint[1..]}Query";

    /// <summary>
    /// Exported shapes in the API clients that carry a list field beside a
    /// boundary field — the offset shape (<c>count</c> with <c>offset</c> /
    /// <c>limit</c>), the gathered shape (<c>count</c> with <c>complete</c>) or
    /// the cursor shape (<c>nextCursor</c>). A bare <c>count</c> does not trip
    /// it.
    /// </summary>
    private static string[] BoundedResponses(string[] clients) =>
        clients
            .SelectMany(text => ExportedShape.Matches(text).AsEnumerable())
            .Where(declaration => IsBounded(declaration.Groups["body"].Value))
            .Select(declaration => declaration.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsBounded(string body) =>
        body.Contains("[]", StringComparison.Ordinal)
        && (Mentions(body, "nextCursor")
            || (Mentions(body, "count")
                && (Mentions(body, "offset") || Mentions(body, "limit") || Mentions(body, "complete"))));

    /// <summary>
    /// The text of every <c>*.api.ts</c> under <see cref="ApiClients"/>,
    /// subdirectories included — a client filed one level down is still a
    /// producer.
    /// </summary>
    private static string[] ApiClientSources()
    {
        string directory = Path.Combine(RepositoryRoot().FullName, ApiClients);

        return Directory.EnumerateFiles(directory, "*.api.ts", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();
    }

    /// <summary>
    /// Every directory under <c>apps/</c> that is a workspace package. The
    /// package manifest is what makes it an app; a bare directory is not one.
    /// </summary>
    private static string[] AppDirectories() =>
        Directory.EnumerateDirectories(Path.Combine(RepositoryRoot().FullName, "apps"))
            .Where(app => File.Exists(Path.Combine(app, "package.json")))
            .OrderBy(app => app, StringComparer.Ordinal)
            .ToArray();

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

        return AppDirectories()
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
        && !IsTest(path)
        && !(path.StartsWith($"{ApiClients}/", StringComparison.Ordinal)
            && path.EndsWith(".api.ts", StringComparison.Ordinal));

    /// <summary>
    /// Both test conventions in the repository, <c>*.test.ts(x)</c> and the
    /// single <c>*.spec.ts</c>. Spec 065 says tests are outside the consumer
    /// sweep; naming only one convention made the document wrong rather than the
    /// rule stricter.
    /// </summary>
    private static bool IsTest(string path) =>
        path.EndsWith(".test.ts", StringComparison.Ordinal)
        || path.EndsWith(".test.tsx", StringComparison.Ordinal)
        || path.EndsWith(".spec.ts", StringComparison.Ordinal)
        || path.EndsWith(".spec.tsx", StringComparison.Ordinal);

    /// <summary>
    /// Forward slashes throughout, and every path comparison in this file is
    /// written against them. <c>Path.GetRelativePath</c> returns the
    /// <em>platform</em> separator, so a filter written with a backslash literal
    /// is green on a Windows developer machine and red on Linux CI — the worst
    /// direction for a guard to break, because it passes exactly where nobody
    /// looks.
    /// </summary>
    private static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

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

    /// <summary>
    /// One register row: the bounded response, the hook that produces it, and
    /// the field a caller must read to know whether it got everything.
    /// </summary>
    private sealed record BoundedResponse(string ResponseType, string Hook, string BoundaryField);
}
