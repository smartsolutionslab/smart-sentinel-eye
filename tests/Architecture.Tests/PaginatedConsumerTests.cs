using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the rule spec 048 paid for in an operator-visible defect: <b>a page is
/// not the list</b> (issue 1982, spec 065).
///
/// <para>
/// Three response types in the front-end API clients answer with as much as the
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
/// <b>Producers are found, not named.</b> Every <c>*.api.ts</c> under
/// <c>apps/*/src</c> is read, not only those under <c>apps/shared/src/api</c>.
/// A client filed beside the app that uses it would otherwise declare a bounded
/// response the completeness check never scanned, and — not being under the
/// excluded prefix — would then be swept as a consumer of its own hook, which
/// it passes by construction. A contract arriving entirely unguarded, with no
/// signal, is the outcome this guard exists to make impossible.
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
    private const string ApiClients = "apps/*/src/**/*.api.ts";

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
    /// is matched by counting braces, so a nesting of any depth is read rather
    /// than dropped, and a declaration can neither borrow the next one's brace
    /// nor run away to the end of the file.
    /// </summary>
    private static readonly Regex ExportedShape = new(
        @"^export (?:interface|type) (?<name>\w+)[^{\r\n]*\{(?<body>(?>[^{}]+|\{(?<depth>)|\}(?<-depth>))*(?(depth)(?!)))\}",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The opening line of a named exported shape. A re-export
    /// (<c>export type { … }</c>) names no shape of its own and does not match.
    /// </summary>
    private static readonly Regex ExportedHead = new(
        @"^export (?:interface|type) (?<name>\w+)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Any exported statement, which is where one declaration stops and the
    /// next begins.
    /// </summary>
    private static readonly Regex ExportStatement = new(
        @"^export ",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// An RTK Query read endpoint and the type it answers with. The generated
    /// accessors are derived from the endpoint name, which is how a response
    /// type and its hooks are joined without keeping a second list by hand.
    /// </summary>
    private static readonly Regex QueryDeclaration = new(
        @"(?<endpoint>\w+): build\.query<\s*(?<response>\w+(?:\[\])?)\s*,",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The content of a string literal, which is data rather than mechanism.
    /// Assertion 3 reads code, not the prose the code prints.
    /// </summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\\r\n]|\\.)*""",
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
    /// hook cannot be swept without a row. A file is a consumer if it reaches
    /// the endpoint by <em>any</em> of the accessors RTK Query generates, not
    /// only by the plain hook — see <see cref="AccessorsOf"/>.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(RegisteredHooks))]
    public void Every_consumer_of_a_bounded_response_names_its_boundary_field(string hook, string boundaryField)
    {
        Regex accessors = AccessorsOf(hook);

        string[] silent = FrontendSources()
            .Where(file => accessors.IsMatch(file.Value))
            .Where(file => !Mentions(file.Value, boundaryField))
            .Select(file => file.Key)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        silent.ShouldBeEmpty(
            $"these files reach {hook} — by that name, by its lazy, state or subscription variant, or "
            + $"through endpoints.{EndpointOf(hook)} — and never name '{boundaryField}': "
            + $"{string.Join(", ", silent)}. A page is not the list — {hook} answers with as much as the "
            + $"source could gather, and a view that renders the items without reading '{boundaryField}' "
            + "shows a truncated answer as if it were everything, with nothing on screen to say so "
            + "(spec 048). Read the field and tell the operator what is missing, as CamerasPage and "
            + "LayoutEditorDialog do.");
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
    /// <b>Assertion 2b — every app is actually swept.</b>
    ///
    /// <para>
    /// <see cref="FrontendSources"/> globs <c>apps/*/src</c> and keeps whatever
    /// it finds, nothing included. An app restructured to hold its sources
    /// elsewhere leaves the corpus silently, and every row of assertion 1 then
    /// passes vacuously and permanently — the worst failure available to a
    /// guard, because it is indistinguishable from compliance.
    /// </para>
    ///
    /// <para>
    /// The claim is per app, and it is about <em>files</em> rather than
    /// directories. A <c>src/</c> holding only declarations and tests satisfies
    /// "the directory exists" while contributing nothing, and the aggregate
    /// floor below cannot see it: two healthy apps carry the count on their own
    /// while every consumer in the third quietly leaves the sweep.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_app_contributes_its_sources_to_the_consumer_sweep()
    {
        string[] swept = [.. FrontendSources().Keys];

        string[] unswept = AppDirectories()
            .Select(app => new DirectoryInfo(app).Name)
            .Where(app => !swept.Any(path => path.StartsWith($"apps/{app}/", StringComparison.Ordinal)))
            .OrderBy(app => app, StringComparer.Ordinal)
            .ToArray();

        unswept.ShouldBeEmpty(
            "these apps have a package.json but contribute no file to the consumer sweep: "
            + $"{string.Join(", ", unswept)}. The sweep globs apps/*/src, so an app that keeps its sources "
            + "elsewhere — or under a src/ holding only declarations and tests — is not read at all, and "
            + "every row of assertion 1 passes without looking at it. Teach the sweep the new layout "
            + "rather than letting the guard go quiet.");
    }

    /// <summary>
    /// <b>Assertion 2c — the corpus is not a rounding error.</b>
    ///
    /// <para>
    /// The companion to 2b: every app can contribute a file and the corpus can
    /// still be almost nothing if the file filter stops matching. One vacuous
    /// row is already live and documented — <c>useGetResourceTimelineQuery</c>
    /// has no consumer today — and a documented vacuity does not detect an
    /// undocumented one.
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
    /// <b>Assertion 2d — nothing is dropped for being unreadable.</b>
    ///
    /// <para>
    /// A declaration <see cref="ExportedShape"/> cannot read does not match
    /// partially — it does not match at all, so it never reaches the
    /// completeness check and its consumers are never swept. Silently, and in
    /// the unsafe direction. This arm makes an unreadable shape loud instead:
    /// the matcher gets taught the shape, rather than the contract being left
    /// outside the guard because nobody noticed it had fallen out.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_exported_shape_in_the_api_clients_is_readable_by_the_matcher()
    {
        string[] unreadable = ApiClientSources()
            .SelectMany(UnreadableShapes)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        unreadable.ShouldBeEmpty(
            $"these shapes in {ApiClients} open a brace the shape matcher cannot read: "
            + $"{string.Join(", ", unreadable)}. A declaration it cannot read is not read partially, it is "
            + "not read at all — so it never reaches the completeness check, gets no register row, and "
            + "every consumer of its hook goes unswept on a green build. Teach the matcher the shape.");
    }

    /// <summary>
    /// <b>Assertion 3 — the gate has no soft edge.</b>
    ///
    /// <para>
    /// FR-005 makes a necessary departure a <em>blocked outcome</em> a human
    /// accepts in writing, not a line added here, and the failure ADR-0144 names
    /// is an agent reaching green by weakening a gate.
    /// </para>
    ///
    /// <para>
    /// It reads code, not prose: comment lines, attribute lines and the content
    /// of string literals are all outside the scan. Failure messages are prose,
    /// and prose about this rule necessarily uses this rule's vocabulary — a
    /// scan that read them would turn "the register ships with no allow-list",
    /// which is FR-005's own wording, into a red build for a wording change.
    /// The price is that a mechanism named <em>only</em> inside a literal — a
    /// baseline file's path, with no identifier spelling it — is not seen. A
    /// mechanism needs a name in code; prose does not.
    /// </para>
    ///
    /// <para>
    /// It polices a vocabulary, not a mechanism. It catches the obvious move by
    /// its spelling; an author who names the same thing something else walks
    /// past it. Ten lines is a fair price for making the obvious move loud,
    /// which is the move an agent under pressure to reach green makes — but it
    /// is not a proof that no soft edge can be added. If it proves awkward in
    /// practice it should be removed by a human with a stated reason, not
    /// quietly.
    /// </para>
    ///
    /// <para>
    /// The rows are distinct under the case-insensitive comparison used here.
    /// Two of them were not: the camel-cased spellings of the first and third
    /// were the same cases twice, and have been spent on spellings nothing
    /// covered. <c>ignore</c> is not among them —
    /// <c>StringComparison.OrdinalIgnoreCase</c> contains it, so the row would
    /// be red on arrival and for the wrong reason.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("allowlist")]
    [InlineData("whitelist")]
    [InlineData("skiplist")]
    [InlineData("baseline")]
    [InlineData("exempt")]
    [InlineData("waiver")]
    [InlineData("waived")]
    [InlineData("knownViolation")]
    [InlineData("suppress")]
    [InlineData("#pragma warning disable")]
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
    /// Every accessor RTK Query generates for one read endpoint, plus the raw
    /// endpoint handle. One <c>build.query</c> yields four hooks —
    /// <c>useXQuery</c>, <c>useLazyXQuery</c>, <c>useXQueryState</c>,
    /// <c>useXQuerySubscription</c> — and <c>endpoints.x.initiate</c> is always
    /// available. Matching the plain hook alone let a consumer switch to
    /// <c>useLazyXQuery</c> and leave the sweep on a green build, because the
    /// lazy spelling does not contain the plain one as a substring.
    /// </summary>
    private static Regex AccessorsOf(string hook)
    {
        string endpoint = EndpointOf(hook);
        string stem = char.ToUpperInvariant(endpoint[0]) + endpoint[1..];

        return new Regex(
            $@"\buse(?:Lazy)?{stem}Query(?:State|Subscription)?\b|\bendpoints\.{endpoint}\b",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The endpoint a register row's hook was generated from — the inverse of
    /// <see cref="HookFor"/>.
    /// </summary>
    private static string EndpointOf(string hook)
    {
        hook.ShouldMatch(
            @"^use\w+Query$",
            $"'{hook}' is not a generated RTK Query hook name, so no endpoint can be recovered from it. "
            + "A register row names the hook RTK Query generates: use + the capitalised endpoint + Query.");

        string stem = hook[3..^5];
        return char.ToLowerInvariant(stem[0]) + stem[1..];
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
    /// Named exported shapes in one client whose declaration opens a brace but
    /// which <see cref="ExportedShape"/> did not match. A declaration is taken
    /// to run from its head to the next exported statement, so a brace-free
    /// alias — <c>export type RuleState = 'Draft' | 'Active';</c> — is not
    /// reported, and a shape does not borrow the braces of the code after it.
    /// </summary>
    private static string[] UnreadableShapes(string text)
    {
        HashSet<string> readable =
            [.. ExportedShape.Matches(text).Select(declaration => declaration.Groups["name"].Value)];
        int[] boundaries = [.. ExportStatement.Matches(text).Select(statement => statement.Index)];

        return ExportedHead.Matches(text)
            .Where(head => !readable.Contains(head.Groups["name"].Value))
            .Where(head => Declaration(text, head.Index, boundaries).Contains('{', StringComparison.Ordinal))
            .Select(head => head.Groups["name"].Value)
            .ToArray();
    }

    private static string Declaration(string text, int start, int[] boundaries) =>
        text[start..boundaries.FirstOrDefault(boundary => boundary > start, text.Length)];

    /// <summary>
    /// The text of every <c>*.api.ts</c> under <c>apps/*/src</c>,
    /// subdirectories included — a client filed one level down, or beside the
    /// app that consumes it, is still a producer.
    /// </summary>
    private static string[] ApiClientSources()
    {
        string[] clients = FrontendFiles()
            .Where(IsApiClient)
            .Select(ReadRepositoryFile)
            .ToArray();

        clients.ShouldNotBeEmpty(
            $"no file matching {ApiClients} was found, so the completeness check has nothing to compare "
            + "the register against and passes on an empty list. The API clients moved — point the sweep "
            + "at where they now live rather than leaving it reading nothing.");

        return clients;
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
    /// Every TypeScript file under <c>apps/*/src</c>, by repository-relative
    /// path, in ordinal order. One enumeration behind both halves of the guard:
    /// what it yields is either a producer or a consumer candidate, so a new
    /// client cannot be scanned by neither.
    /// </summary>
    private static string[] FrontendFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return AppDirectories()
            .Select(app => Path.Combine(app, "src"))
            .Where(Directory.Exists)
            .SelectMany(source => Directory.EnumerateFiles(source, "*.ts*", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Every source that could consume a bounded response, keyed by
    /// repository-relative path. Test files and the producing API clients are
    /// left out: the producer names both the hook and the field by
    /// construction, and a test asserting on a truncated fixture is doing its
    /// job.
    /// </summary>
    private static Dictionary<string, string> FrontendSources() =>
        FrontendFiles()
            .Where(IsConsumerCandidate)
            .ToDictionary(path => path, ReadRepositoryFile, StringComparer.Ordinal);

    /// <summary>
    /// A producer, recognised by its name rather than by where it is filed. The
    /// path prefix this once tested made a client outside
    /// <c>apps/shared/src/api</c> both unscanned as a producer and swept as a
    /// consumer of its own hook.
    /// </summary>
    private static bool IsApiClient(string path) =>
        path.EndsWith(".api.ts", StringComparison.Ordinal) && !IsTest(path);

    private static bool IsConsumerCandidate(string path) =>
        (path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal))
        && !path.EndsWith(".d.ts", StringComparison.Ordinal)
        && !IsTest(path)
        && !IsApiClient(path);

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
    /// Lines that are neither commentary nor attribute metadata, with the
    /// content of every string literal removed — what is left is the code that
    /// could actually carry a mechanism, rather than the prose it prints.
    /// </summary>
    private static IEnumerable<string> ExecutableLines(string source) =>
        source.Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                && !line.StartsWith('['))
            .Select(line => StringLiteral.Replace(line, "\"\""));

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
