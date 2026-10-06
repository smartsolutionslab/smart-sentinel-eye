using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 302 (#2424/#2492) — M16 in <c>StatusProducerDeclarationTests</c>'
/// census: <b>an endpoint whose handler runs an idempotent request declares
/// the <c>422</c> that request can answer when a caller's key is bound to a
/// different one.</b>
///
/// <para>
/// <c>IdempotentRequest.ExecuteAsync</c>/<c>ExecuteCreateAsync</c> answer
/// <c>422 IDEMPOTENCY_KEY_REUSED</c> when a claim comes back
/// <c>IdempotencyOutcome.Mismatched</c> (spec 302 FR-005) — before this change
/// the only two statuses either method could answer were <c>409</c>
/// (in-progress) and whatever <c>work</c>/<c>replay</c> itself produces. Twelve
/// call sites across seven contexts run one of these two methods; this guard
/// reads each mapping's own handler body (one hop, like
/// <c>PreconditionDeclarationTests</c> and <c>RouteValueRefusalDeclarationTests</c>
/// before it) and fails any of the twelve that does not declare the 422 on its
/// own <c>Map…</c> chain.
/// </para>
///
/// <para>
/// <b>Red on arrival, all twelve</b> — the census row and this guard are
/// written before the 422 branch exists at all, so every one of the twelve
/// chains is reported as missing a declaration it will need the moment
/// <c>IdempotentRequest</c> gains it (ADR-0139: the test observes the gap
/// before the fix, not after).
/// </para>
///
/// <para>
/// Shares <c>RouteChainReader</c>, <c>SourceMask</c> and <c>RepositorySource</c>
/// with its siblings (spec 190, issue #2257) rather than carrying its own
/// copies of the chain reader, the masker and the repository walk.
/// </para>
/// </summary>
public class IdempotentKeyReuseDeclarationTests
{
    private const string GuardSource = "tests/Architecture.Tests/IdempotentKeyReuseDeclarationTests.cs";
    private const string EndpointFileSuffix = "Endpoints.cs";
    private const string DeclarationToken = "Status422UnprocessableEntity";

    /// <summary>
    /// The twelve call sites spec 302's table names: one per context file that
    /// maps the keyed create/rotate — Automation, CameraCatalog,
    /// EventIngestion (three: manual events, event sources, event types),
    /// Identity (three: devices, kiosks, webhook rotation), LayoutComposition
    /// (two: layouts, walls), OverlayDesigner and SystemVariables.
    /// </summary>
    private const int IdempotentRequestEndpointCount = 12;

    /// <summary>The twelve endpoints above live in twelve distinct mapping files.</summary>
    private const int IdempotentRequestFileCount = 12;

    private static readonly Regex MappingCall = new(
        @"\.Map(?<verb>Get|Post|Put|Patch|Delete)\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ClassDeclaration = new(
        @"\bclass\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The two methods that can answer the key-reuse refusal. Nothing else in
    /// the product calls into <c>IdempotentRequest</c>; if something ever does,
    /// this sweep does not see it, and that assumption is recorded here rather
    /// than hidden.
    /// </summary>
    private static readonly Regex HelperCall = new(
        @"IdempotentRequest\.(?<method>ExecuteAsync|ExecuteCreateAsync)\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DeclaresReuseRefusal = new(
        @"\.Produces(?:Validation)?Problem\(\s*StatusCodes\.Status422UnprocessableEntity\s*\)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Lazy<Surface> TheSurface = new(Read);

    /// <summary>The endpoint files, found by glob and never named, one theory case each.</summary>
    public static TheoryData<string> EndpointFiles()
    {
        TheoryData<string> data = [];
        foreach (string file in TheSurface.Value.EndpointSources)
        {
            data.Add(file);
        }

        return data;
    }

    // ---- A1: nothing resolves to a pass by default -------------------------

    /// <summary>
    /// Every mapping this guard reads resolves to exactly one handler body — a
    /// guard that quietly skips what it cannot parse is the guard that was not
    /// there.
    /// </summary>
    [Fact]
    public void Every_route_mapping_resolves_to_one_handler_body_the_guard_can_read()
    {
        IReadOnlyList<ResolvedMapping> mappings = TheSurface.Value.Routes;

        mappings.Count.ShouldBeGreaterThan(
            0,
            "no route mappings were found under src/*/Api. That is the reader failing, not the product: "
            + "every later assertion in this file would then pass over an empty set.");

        string[] unresolved = mappings
            .Where(m => m.Failure is not null)
            .Select(m => $"{Describe(m.Mapping)}: {m.Failure}")
            .ToArray();

        unresolved.ShouldBeEmpty(
            "these mappings do not resolve to exactly one handler body:"
            + Environment.NewLine + string.Join(Environment.NewLine, unresolved) + Environment.NewLine
            + "This guard reads the handler's body to decide whether the endpoint runs an idempotent "
            + "request, so a mapping it cannot resolve is a mapping it cannot judge — and it fails rather "
            + "than passing.");
    }

    // ---- A3: the claim -------------------------------------------------------

    /// <summary>
    /// <b>The claim.</b> An endpoint whose handler calls
    /// <c>IdempotentRequest.ExecuteAsync</c> or <c>ExecuteCreateAsync</c>
    /// declares <c>422</c> on its own mapping chain.
    /// </summary>
    [Fact]
    public void Every_endpoint_that_runs_an_idempotent_request_declares_the_422_it_answers()
    {
        ResolvedMapping[] running = TheSurface.Value.Routes.Where(RunsIdempotentRequest).ToArray();

        running.Length.ShouldBeGreaterThan(
            0,
            "no endpoint under src/*/Api was read as running IdempotentRequest, which cannot be true while "
            + "twelve call sites exist. The body reader has stopped finding the helper calls.");

        string[] offenders = running
            .Where(m => !DeclaresReuseRefusal.IsMatch(m.Mapping.Chain))
            .Select(m => $"{Describe(m.Mapping)} -> {m.Mapping.ContainingClass}.{m.Mapping.HandlerArgument} "
                + $"[{string.Join(", ", m.Calls.Select(c => $"{c.File}:{c.Line} IdempotentRequest.{c.Method}"))}]")
            .ToArray();

        offenders.ShouldBeEmpty(
            $"{offenders.Length} endpoint(s) run IdempotentRequest and do not declare 422:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders) + Environment.NewLine
            + "IdempotentRequest answers 422 IDEMPOTENCY_KEY_REUSED when a caller's Idempotency-Key is bound "
            + "to a different request (spec 302 FR-005), and every one of these handlers can reach that "
            + "branch. Without the declaration the generated OpenAPI asserts that a status the endpoint can "
            + "answer cannot happen. Add '.ProducesProblem(StatusCodes.Status422UnprocessableEntity)' to "
            + "the chain above.");
    }

    // ---- A4: the mirror ------------------------------------------------------

    /// <summary>
    /// The control that A3's resolution is not failing open: a chain that
    /// declares 422 and whose handler never runs an idempotent request would
    /// turn every correct declaration into a mirror violation if the reader
    /// returned "runs nothing" for everything.
    /// </summary>
    [Fact]
    public void No_endpoint_declares_422_without_running_an_idempotent_request()
    {
        IReadOnlyList<ResolvedMapping> mappings = TheSurface.Value.Routes;

        string[] offenders = mappings
            .Where(m => DeclaresReuseRefusal.IsMatch(m.Mapping.Chain) && !RunsIdempotentRequest(m))
            .Select(m => $"{Describe(m.Mapping)} -> {m.Mapping.ContainingClass}.{m.Mapping.HandlerArgument}")
            .ToArray();

        offenders.ShouldBeEmpty(
            $"{offenders.Length} endpoint(s) declare 422 and run no IdempotentRequest call:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders) + Environment.NewLine
            + "A declared 422 that nothing in the handler can answer is the opposite direction from the "
            + "missing-declaration failure, and has its own cause.");
    }

    // ---- A5, A6: the two independent sweeps ----------------------------------

    /// <summary>
    /// Every <c>IdempotentRequest.Execute*Async</c> call site under <c>src/*/Api</c>
    /// sits inside a mapped handler. The walk and a flat file sweep count the
    /// same thing two ways, so hoisting a call into a private helper the
    /// handler calls makes them disagree rather than pass quietly.
    /// </summary>
    [Fact]
    public void Every_idempotent_request_call_site_sits_in_a_mapped_handler()
    {
        Surface surface = TheSurface.Value;
        int swept = surface.Files.Sum(file => HelperCall.Count(surface.Masked[file]));
        int walked = surface.Routes
            .Where(m => m.Handler is not null)
            .DistinctBy(m => (m.Handler!.File, m.Handler.BodyStart))
            .Sum(m => m.Calls.Count);

        swept.ShouldBe(
            IdempotentRequestEndpointCount,
            $"a flat sweep of src/*/Api found {swept} IdempotentRequest.Execute*Async call sites, not "
            + $"{IdempotentRequestEndpointCount}. The population moved: an endpoint started or stopped "
            + "running an idempotent request, or an endpoint file left these directories. Re-measure and "
            + "edit this number in the same diff.");

        walked.ShouldBe(
            swept,
            $"the mapping walk found {walked} IdempotentRequest call sites inside mapped handler bodies; "
            + $"the file sweep found {swept}. A call that moved out of a mapped handler — into a private "
            + "helper, a local function — is invisible to the walk, and the endpoint is then judged as "
            + "running nothing.");
    }

    /// <summary>Every 422 declaration under these directories sits in a mapping's own chain.</summary>
    [Fact]
    public void Every_422_declaration_under_the_api_directories_sits_in_a_mapping_chain()
    {
        Surface surface = TheSurface.Value;
        int swept = surface.Files.Sum(file => Occurrences(surface.Masked[file], DeclarationToken));
        int walked = surface.Routes.Sum(m => Occurrences(m.Mapping.Chain, DeclarationToken));

        walked.ShouldBe(
            swept,
            $"the mapping walk found {walked} Status422UnprocessableEntity declarations inside mapping "
            + $"chains; a flat sweep of src/*/Api found {swept}. A declaration the walk cannot see — in a "
            + "shared convention, an endpoint filter, a metadata helper — is one A3 does not credit, and "
            + "would report its endpoint as declaring nothing while the document is in fact correct.");
    }

    // ---- A7: the pinned corpus ------------------------------------------------

    /// <summary>The corpus, pinned so a file leaving the glob does not shrink both sides of every comparison silently.</summary>
    [Fact]
    public void The_idempotent_request_corpus_is_twelve_endpoints_across_twelve_files()
    {
        ResolvedMapping[] running = TheSurface.Value.Routes.Where(RunsIdempotentRequest).ToArray();
        string[] files = running
            .Select(m => m.Mapping.File)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        running.Length.ShouldBe(
            IdempotentRequestEndpointCount,
            $"{running.Length} mappings were read as running IdempotentRequest, not "
            + $"{IdempotentRequestEndpointCount}:" + Environment.NewLine
            + string.Join(Environment.NewLine, running.Select(m => Describe(m.Mapping))));

        files.Length.ShouldBe(
            IdempotentRequestFileCount,
            $"the {IdempotentRequestEndpointCount} endpoints live in {files.Length} file(s), not "
            + $"{IdempotentRequestFileCount}: {string.Join(", ", files)}.");
    }

    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void Every_endpoint_file_contributes_at_least_one_mapping(string file)
    {
        ResolvedMapping[] mappings = TheSurface.Value.Routes
            .Where(m => string.Equals(m.Mapping.File, file, StringComparison.Ordinal))
            .ToArray();

        mappings.Length.ShouldBeGreaterThan(
            0,
            $"'{file}' is named like an endpoint file and yielded no route mapping this guard can read.");
    }

    // ---- no soft edge ----------------------------------------------------

    [Theory]
    [InlineData("allowlist")]
    [InlineData("whitelist")]
    [InlineData("skiplist")]
    [InlineData("exempt")]
    [InlineData("waiver")]
    [InlineData("waived")]
    [InlineData("knownViolation")]
    [InlineData("suppress")]
    [InlineData("#pragma warning disable")]
    public void The_guard_offers_no_way_to_excuse_an_endpoint(string mechanism)
    {
        string[] offenders = RepositorySource.ExecutableLines(ReadRepositoryFile(GuardSource))
            .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the guard's own code names '{mechanism}': {string.Join(" | ", offenders)}. That reads as a "
            + "way to excuse an endpoint from the rule, and a rule with a soft edge is a review convention "
            + "wearing a build failure's clothes.");
    }

    // ---- reading the surface -----------------------------------------------

    private static bool RunsIdempotentRequest(ResolvedMapping mapping) => mapping.Calls.Count > 0;

    private static string Describe(RouteMapping mapping) =>
        $"{mapping.File}:{mapping.Line} {mapping.Verb} {mapping.Route}";

    private static int Occurrences(string text, string token)
    {
        int count = 0;
        int index = text.IndexOf(token, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadRepositoryFile(string relative) =>
        File.ReadAllText(Path.Combine(RepositorySource.Root().FullName, relative))
            .Replace("\r", string.Empty, StringComparison.Ordinal);

    private static Surface Read()
    {
        DirectoryInfo root = RepositorySource.Root();
        List<string> files = ApiSourceFiles(root);

        Dictionary<string, string> masked = new(StringComparer.Ordinal);
        Dictionary<string, string> text = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string body = File.ReadAllText(Path.Combine(root.FullName, file))
                .Replace("\r", string.Empty, StringComparison.Ordinal);
            text[file] = body;
            masked[file] = SourceMask.Apply(body, MaskStrictness.CommentsAndLiteralInteriors);
        }

        List<RouteChainReader.ClassSpan> classes = files.SelectMany(file => ClassSpans(file, masked[file])).ToList();
        List<ResolvedMapping> mappings = files
            .SelectMany(file => Mappings(file, text[file], masked[file], classes))
            .Select(mapping => Resolve(mapping, classes, masked))
            .ToList();

        return new Surface(
            files,
            files.Where(f => f.EndsWith(EndpointFileSuffix, StringComparison.Ordinal)).ToList(),
            masked,
            mappings);
    }

    private static List<string> ApiSourceFiles(DirectoryInfo root)
    {
        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateDirectories(src)
            .Select(context => Path.Combine(context, "Api"))
            .Where(Directory.Exists)
            .SelectMany(api => Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
            .Select(file => RepositorySource.RelativePath(root, file))
            .Where(file => !file.Contains("/obj/", StringComparison.Ordinal)
                && !file.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<RouteChainReader.ClassSpan> ClassSpans(string file, string masked)
    {
        foreach (Match declaration in ClassDeclaration.Matches(masked))
        {
            int open = masked.IndexOf('{', declaration.Index + declaration.Length);
            if (open < 0)
            {
                continue;
            }

            int close = RouteChainReader.Balanced(masked, open, '{', '}');
            if (close > 0)
            {
                yield return new RouteChainReader.ClassSpan(file, declaration.Groups["name"].Value, open, close);
            }
        }
    }

    private static IEnumerable<RouteMapping> Mappings(
        string file,
        string text,
        string masked,
        IReadOnlyList<RouteChainReader.ClassSpan> classes)
    {
        foreach (Match call in MappingCall.Matches(masked))
        {
            int open = call.Index + call.Length - 1;
            int close = RouteChainReader.Balanced(masked, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            int end = RouteChainReader.StatementEnd(
                masked, close + 1, ChainEndSentinel.NotFound, ChainLiteralHandling.AlreadyMasked);
            string chain = end < 0 ? masked[call.Index..] : masked[call.Index..end];

            List<(int Start, int End)> arguments = RouteChainReader.SplitArguments(masked, open + 1, close);
            string route = arguments.Count > 0 ? text[arguments[0].Start..arguments[0].End].Trim() : string.Empty;
            string handler = arguments.Count > 1 ? text[arguments[1].Start..arguments[1].End].Trim() : string.Empty;

            yield return new RouteMapping(
                file,
                RouteChainReader.LineOf(masked, call.Index),
                call.Groups["verb"].Value.ToUpperInvariant(),
                RouteChainReader.Unquote(route),
                DeclaringClass(classes, file, call.Index),
                handler,
                chain);
        }
    }

    private static string DeclaringClass(IReadOnlyList<RouteChainReader.ClassSpan> classes, string file, int index) =>
        classes
            .Where(c => string.Equals(c.File, file, StringComparison.Ordinal) && c.Start < index && index < c.End)
            .OrderBy(c => c.End - c.Start)
            .Select(c => c.Name)
            .FirstOrDefault() ?? string.Empty;

    private static ResolvedMapping Resolve(
        RouteMapping mapping,
        IReadOnlyList<RouteChainReader.ClassSpan> classes,
        Dictionary<string, string> masked)
    {
        if (mapping.ContainingClass.Length == 0)
        {
            return Unreadable(mapping, "the mapping is not inside a class declaration this reader can find");
        }

        RouteChainReader.HandlerResolution resolution = RouteChainReader.HandlerBodyFor(
            mapping.ContainingClass, mapping.HandlerArgument, mapping.File, classes, masked);

        if (!resolution.IsBareMethodGroupName)
        {
            return Unreadable(
                mapping, $"the handler argument '{mapping.HandlerArgument}' is not a bare method-group name");
        }

        if (resolution.Body is null)
        {
            if (resolution.Candidates.Count == 0)
            {
                return Unreadable(
                    mapping,
                    $"'{mapping.ContainingClass}.{mapping.HandlerArgument}' resolves to no method declaration");
            }

            return Unreadable(
                mapping,
                $"'{mapping.ContainingClass}.{mapping.HandlerArgument}' resolves to "
                + $"{resolution.Candidates.Count} method declarations");
        }

        RouteChainReader.HandlerBody body = resolution.Body;
        List<IdempotentCall> calls = HelperCall.Matches(body.Body)
            .Select(match => new IdempotentCall(
                match.Groups["method"].Value,
                body.File,
                RouteChainReader.LineOf(masked[body.File], body.BodyStart + match.Index)))
            .ToList();

        return new ResolvedMapping(mapping, body, calls, null);
    }

    private static ResolvedMapping Unreadable(RouteMapping mapping, string failure) =>
        new(mapping, null, [], failure);

    private sealed record Surface(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> EndpointSources,
        IReadOnlyDictionary<string, string> Masked,
        IReadOnlyList<ResolvedMapping> Routes);

    private sealed record RouteMapping(
        string File,
        int Line,
        string Verb,
        string Route,
        string ContainingClass,
        string HandlerArgument,
        string Chain);

    private sealed record IdempotentCall(string Method, string File, int Line);

    private sealed record ResolvedMapping(
        RouteMapping Mapping,
        RouteChainReader.HandlerBody? Handler,
        IReadOnlyList<IdempotentCall> Calls,
        string? Failure);
}
