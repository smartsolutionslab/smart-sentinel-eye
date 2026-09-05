using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the rule spec 070 was written for: <b>an endpoint names the scope it
/// needs</b> (issue 2087; #850 was closed as delivered for its Identity label).
///
/// <para>
/// Eighteen endpoints already spell it — <c>.WithSummary("… Required scope:
/// sse.x.y")</c> — and thirty-three do not. Nothing enforced it, and the price
/// of that is issue <b>2070</b>: <c>sse.variables.read</c> exists in the
/// catalogue, is provisioned in the realm and granted to three clients, and is
/// required by nothing. Two of the three endpoints that should require it carry
/// a summary describing fab filtering and archived rows in careful detail and
/// simply never mention a scope. The prose was written; the omission was
/// invisible, because there was no place the omission had to show up. A phase-6
/// review of an unrelated PR found it by accident.
/// </para>
///
/// <para>
/// <b>What it asserts.</b> Every route-handler mapping under <c>src/*/Api</c>
/// resolves to an effective authorization — a scope, explicit anonymity, or a
/// bare <c>.RequireAuthorization()</c> naming nothing — and the summary must
/// agree with it: a scoped endpoint contains <c>Required scope: </c> followed by
/// the literal the constant it cites resolves to, and an anonymous one contains
/// <c>No OIDC scope:</c> followed by what authenticates it instead.
/// </para>
///
/// <para>
/// <b>Both registers are derived, neither is typed in.</b> The scope literals
/// come from reflection over <see cref="Scope"/>'s nested constants, not from
/// <c>Scope.All</c> — <c>All</c> is a hand-maintained list in the same file, and
/// a guard checked against it would agree with a list rather than with the
/// constants the endpoints actually cite. The endpoints come from a glob, never
/// from a list of names, and the count is checked against a sweep of every
/// <c>.cs</c> file under <c>src/*/Api</c>, so a mapping in a file the glob does
/// not match is red rather than silent.
/// </para>
///
/// <para>
/// <b><see cref="UnenforcedByDesign"/> is a register checked both ways, which is
/// the opposite of an allow-list — do not simplify it into one.</b> An
/// allow-list is read in one direction: a row silences a failure and then sits
/// there forever, and the day the code is fixed the row is the only thing that
/// remembers a problem nobody has any more. This register is read in both. A
/// bare-authorization endpoint missing from it fails
/// (<see cref="Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue"/>),
/// <em>and</em> a row that no longer matches a bare-authorization endpoint also
/// fails
/// (<see cref="Every_registered_route_still_enforces_no_scope"/>).
/// So the three rows it ships with — the #2070 routes, which have no scope to
/// name and therefore cannot satisfy the main rule — turn red the moment #2070
/// is fixed, and stay red until they are deleted. That is how this guard makes
/// #2070 build-visible without touching it. Adding a row is a reviewable line
/// in a diff that cites an open issue, not a suppression.
/// </para>
///
/// <para>
/// <b>Under-recognition is the failure mode, so it is reported by name.</b> A
/// mapping whose chain the guard cannot read is a mapping it does not check, and
/// a guard that quietly skips what it cannot parse is the guard that was not
/// there. An unreadable receiver, an unreadable authorization argument and an
/// unreadable summary argument each resolve to a <em>failure</em> naming the
/// mapping, never to a pass. Every endpoint file is asserted non-empty
/// individually rather than through one total, because a total lets one file
/// stop being read while the others carry the count.
/// </para>
///
/// <para>
/// <b>What this guard provably cannot catch.</b>
/// </para>
///
/// <para>
/// <i>It is a source scan, not a running application.</i> It reads the fluent
/// chain in the file. An endpoint mapped from a helper in another file, or a
/// scope chosen at run time from configuration, would be judged on the wrong
/// text or not at all. No such indirection exists today, and the denominator
/// assertion fails the build if a mapping appears outside the files swept.
/// </para>
///
/// <para>
/// <i>It cannot see policy composition.</i> <c>RequireAuthorization(scope)</c>
/// is taken at face value as "requires that scope". If <c>AddScopePolicies</c>
/// were changed to map a policy name onto different claims, this guard would
/// still pass; that is <c>KioskScopeParityTests</c>' territory, not this one.
/// </para>
///
/// <para>
/// <i>It checks that the sentence is present and consistent, not that the prose
/// around it is true.</i> A summary reading <c>Required scope:
/// sse.cameras.read</c> on an endpoint enforcing <c>sse.cameras.read</c> passes
/// however wrong the rest of the sentence is. Nothing here reads the
/// description.
/// </para>
///
/// <para>
/// <i>It says nothing about whether the scope is the</i> right <i>scope.</i> An
/// endpoint enforcing <c>sse.cameras.read</c> for a write is perfectly
/// consistent and perfectly wrong; only a human or a security review catches
/// that.
/// </para>
///
/// <para>
/// <i>It reads the group a mapping's receiver was assigned from, in the same
/// file, by variable name.</i> Two files declare two groups on the same route
/// prefix with different scopes — <c>RulesEndpoints</c> and
/// <c>CameraEndpoints</c> — so binding by prefix would be green and wrong. A
/// group assigned across files, or reassigned, is not read and reports as
/// unreadable.
/// </para>
///
/// <para>
/// The residual is deliberate. This removes the failure mode that actually
/// occurred — a scope silently absent from both the chain and the prose — and
/// claims nothing more.
/// </para>
/// </summary>
public class EndpointScopeDeclarationTests
{
    private const string GuardSource = "tests/Architecture.Tests/EndpointScopeDeclarationTests.cs";
    private const string EndpointGlob = "src/*/Api/**/*Endpoints.cs";
    private const string ApiGlob = "src/*/Api/**/*.cs";

    /// <summary>
    /// The label, spelled exactly as the eighteen conformant endpoints spell it.
    /// No document declares it; the code is unanimous, which is the only
    /// authority available and a better one than a document nobody checks.
    /// </summary>
    private const string ScopeLabel = "Required scope: ";

    /// <summary>
    /// What an endpoint outside OIDC must say instead. Silent anonymity is the
    /// failure this prevents: both of today's anonymous endpoints are
    /// token-authenticated by other means and neither says so in metadata.
    /// </summary>
    private const string AnonymousLabel = "No OIDC scope:";

    private const string NoSummary = "(no .WithSummary in the chain)";

    /// <summary>
    /// The routes that enforce no scope at all, each against the open issue that
    /// will fix it. <b>Read in both directions</b> — see the class doc. These
    /// three are issue 2070: <c>sse.variables.read</c> exists, is granted, and
    /// is required by nothing.
    ///
    /// <para>
    /// They are here rather than fixed because adding the missing
    /// <c>RequireAuthorization</c> is a change to runtime authorization, and
    /// spec 070 declares itself behaviour-preserving in <c>src/</c>. Fixing
    /// 2070 deletes these rows; the completeness half fails if it does not.
    /// </para>
    /// </summary>
    private static readonly UnenforcedRoute[] UnenforcedByDesign =
    [
        new("GET", "/system-variables", 2070),
        new("GET", "/system-variables/snapshot", 2070),
        new("GET", "/system-variables/{name}", 2070),
    ];

    /// <summary>
    /// Every scope constant, by its full path and by every unambiguous tail of
    /// it. Built by reflection, so no scope string is typed into this file.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ScopeLiterals = BuildScopeCatalogue();

    /// <summary>
    /// A route-handler registration and the variable it was registered on. The
    /// receiver is captured because it, not the route prefix, is what binds a
    /// mapping to its group.
    /// </summary>
    private static readonly Regex MappingSite = new(
        @"\b(?<receiver>\w+)\s*\.Map(?<verb>Get|Post|Put|Patch|Delete)\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The declaration of a local that a group may have been assigned to. The
    /// statement is only treated as a group once it is seen to contain a
    /// <c>MapGroup</c> call.
    /// </summary>
    private static readonly Regex LocalDeclaration = new(
        @"\b(?:RouteGroupBuilder|var)\s+(?<name>\w+)\s*=",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex GroupSite = new(
        @"\.MapGroup\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A dotted constant path such as <c>Scope.Sse.Cameras.Write</c>. Anything
    /// else in the authorization argument is unreadable rather than guessed at.
    /// </summary>
    private static readonly Regex ConstantPath = new(
        @"^[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The scope a summary claims. Trailing punctuation is trimmed so a sentence
    /// that ends in a full stop is read as the scope it names, not as a mismatch.
    /// </summary>
    private static readonly Regex ClaimedScope = new(
        @"Required scope:\s*(?<claimed>[A-Za-z0-9._\-]+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The content of a string literal, which is data rather than mechanism. The
    /// self-scan reads code, not the prose the code prints.
    /// </summary>
    private static readonly Regex StringLiteral = new(
        @"""(?:[^""\\\r\n]|\\.)*""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The endpoint files, found by glob and never named, one theory case each.
    /// A per-file case reports every offender in that file at once, which is
    /// what someone fixing a file wants; per-endpoint cases would produce
    /// thirty-five separate results and bury the shape of the problem.
    /// </summary>
    public static TheoryData<string> EndpointFiles()
    {
        TheoryData<string> data = [];
        foreach (string file in EndpointSourceFiles())
        {
            data.Add(file);
        }

        return data;
    }

    /// <summary>
    /// <b>A1 — the denominator is not chosen by the guard.</b>
    ///
    /// <para>
    /// The mappings enumerated from <see cref="EndpointGlob"/> must account for
    /// every <c>Map*</c> registration in every <c>.cs</c> file under
    /// <c>src/*/Api</c>. An endpoint moved into a file the glob does not match
    /// is then red rather than quietly outside the sweep, which is the only
    /// failure a guard cannot recover from on its own.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_route_handler_mapping_under_the_api_projects_is_enumerated()
    {
        int swept = ApiSourceFiles().Sum(file => MappingSite.Count(Masked(ReadRepositoryFile(file))));
        int enumerated = EndpointSourceFiles().Sum(file => Read(file).Mappings.Length);

        swept.ShouldBeGreaterThan(
            0,
            $"the sweep over {ApiGlob} found no route-handler mapping at all. Every assertion below is "
            + "then green because it read nothing, not because the endpoints are correct. The API "
            + "projects moved, or the sweep stopped matching — fix the sweep before trusting any result.");

        enumerated.ShouldBe(
            swept,
            $"{ApiGlob} registers {swept} route handlers, but the {EndpointGlob} sweep enumerated "
            + $"{enumerated}. A mapping this guard never enumerates is a mapping it never checks, and it "
            + "fails nothing on its way past — the endpoint would ship with no summary, no scope sentence "
            + "and a green build. Teach the sweep where the mapping now lives.");
    }

    /// <summary>
    /// <b>A1b — the scope catalogue is a real catalogue.</b>
    ///
    /// <para>
    /// The companion to A1. Reflection that stops finding constants would leave
    /// every scoped endpoint with an unresolvable scope, and while A3 reports
    /// that, it reports fifty-one endpoints when the fault is one — this says
    /// which of the two it is.
    /// </para>
    /// </summary>
    [Fact]
    public void The_scope_catalogue_is_read_from_the_constants_the_endpoints_cite()
    {
        ScopeLiterals.Keys.ShouldContain(
            $"{nameof(Scope)}.{nameof(Scope.Sse)}.{nameof(Scope.Sse.Cameras)}.{nameof(Scope.Sse.Cameras.Read)}",
            "reflection over ServiceDefaults.Authorization.Scope did not yield the constant path the "
            + "endpoints are written against. The catalogue moved or changed shape, and every scoped "
            + "endpoint would then resolve to nothing — which reads as fifty-one broken endpoints rather "
            + "than one broken reader.");

        ScopeLiterals.Values.Distinct(StringComparer.Ordinal).Count().ShouldBeGreaterThan(
            1,
            "the scope catalogue resolved to fewer than two distinct literals, so it cannot distinguish "
            + "the scope an endpoint enforces from any other. Walk the nested constants, not Scope.All.");
    }

    /// <summary>
    /// <b>A2 — nothing is dropped for being unreadable, and every file is read.</b>
    ///
    /// <para>
    /// The non-emptiness claim is per file rather than in aggregate: a single
    /// total lets one file stop being parsed while the eleven others carry the
    /// count, and the endpoints in it are then unguarded on a green build.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void Every_mapping_in_an_endpoint_file_is_read_rather_than_skipped(string file)
    {
        EndpointFileReading reading = Read(file);

        reading.Mappings.ShouldNotBeEmpty(
            $"{file} matches {EndpointGlob} but yielded no route-handler mapping. Either it registers none "
            + "— in which case it is misnamed — or the chain reader stopped recognising the shape it is "
            + "written in, and every endpoint in it is now unchecked while the suite stays green.");

        reading.Unread.ShouldBeEmpty(
            $"the chain reader could not resolve these registrations in {file}: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, reading.Unread)}"
            + $"{Environment.NewLine}A registration it cannot read is one it does not check — it is not "
            + "treated as anonymous, and it is not treated as scoped, because guessing either way is how a "
            + "guard passes an endpoint it never looked at. Teach the reader the shape, or write the "
            + "registration in one of the shapes it reads.");
    }

    /// <summary>
    /// <b>A3 — every scope an endpoint cites exists in the catalogue.</b>
    ///
    /// <para>
    /// Resolved by reflection over <see cref="Scope"/>. A path that resolves to
    /// nothing is a mapping whose required sentence cannot even be computed, so
    /// it is reported here rather than silently excused from A4.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void Every_scope_an_endpoint_enforces_resolves_against_the_catalogue(string file)
    {
        string[] unresolved = Read(file).Mappings
            .Where(mapping => mapping.Kind == AuthorizationKind.Scoped)
            .Where(mapping => !ScopeLiterals.ContainsKey(mapping.ScopeConstant))
            .Select(mapping => $"{Where(mapping)} cites {mapping.ScopeConstant}")
            .ToArray();

        unresolved.ShouldBeEmpty(
            "these endpoints require an authorization argument that is not a constant of "
            + $"ServiceDefaults.Authorization.Scope: {Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, unresolved)}{Environment.NewLine}"
            + "The catalogue is read by reflection over the nested constants, not from Scope.All, so a "
            + "scope that is granted in the realm but never declared as a constant does not resolve here. "
            + "Declare the constant and cite it, so the scope the endpoint enforces and the scope the "
            + "summary names are the same string by construction.");
    }

    /// <summary>
    /// <b>A4 — a scoped endpoint names its scope.</b>
    ///
    /// <para>
    /// Omission only: a summary that names some <em>other</em> scope is A5's,
    /// and the two never fire on the same endpoint. A reader who gets one
    /// message for both learns less than the guard knows.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void Every_scoped_endpoint_names_the_scope_it_enforces_in_its_summary(string file)
    {
        string[] silent = ScopedWithLiteral(file)
            .Where(pair => ClaimedScopes(pair.Mapping.Summary).Length == 0)
            .Select(pair => Omission(pair.Mapping, pair.Literal))
            .ToArray();

        silent.ShouldBeEmpty(
            "these endpoints enforce a scope their summary never names: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, silent)}{Environment.NewLine}"
            + "The required-scope catalogue is only legible at the surface if the surface says it. "
            + "Eighteen endpoints already spell the sentence exactly this way; nothing enforced it, and "
            + "the gap that opened is issue 2070 — a scope granted in the realm, absent from the chain, "
            + "and absent from prose that described everything else about the endpoint in detail. Append "
            + "the sentence; change no route, handler, policy or Produces declaration.");
    }

    /// <summary>
    /// <b>A5 — a summary that names the wrong scope is worse than one that names
    /// none.</b>
    ///
    /// <para>
    /// An omission leaves a reader to read the chain. A mismatch tells them not
    /// to bother, and is believed. Its own message, as the spec requires.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void No_summary_names_a_scope_other_than_the_one_the_endpoint_enforces(string file)
    {
        string[] misinforming = ScopedWithLiteral(file)
            .SelectMany(pair => ClaimedScopes(pair.Mapping.Summary)
                .Where(claimed => !string.Equals(claimed, pair.Literal, StringComparison.Ordinal))
                .Select(claimed => Mismatch(pair.Mapping, pair.Literal, claimed)))
            .ToArray();

        misinforming.ShouldBeEmpty(
            "these summaries name a scope the endpoint does not enforce: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, misinforming)}{Environment.NewLine}"
            + "This is strictly worse than saying nothing. A missing sentence sends a reader to the "
            + "authorization chain; a wrong one persuades them they have already read it, and they act on "
            + "a scope the endpoint will refuse — or, worse, grant a client a scope it did not need. "
            + "Correct the sentence, or correct the chain, but do not leave them disagreeing.");
    }

    /// <summary>
    /// <b>A6 — an endpoint that enforces no scope is registered against an open
    /// issue.</b>
    ///
    /// <para>
    /// A bare <c>.RequireAuthorization()</c> has no scope to name, so it cannot
    /// satisfy A4. It must therefore be visible some other way, and the register
    /// is that way: a reviewable line citing an issue, in a diff, rather than a
    /// silence.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue()
    {
        string[] unregistered = AllMappings()
            .Where(mapping => mapping.Kind == AuthorizationKind.Bare)
            .Where(mapping => !UnenforcedByDesign.Any(row => Matches(row, mapping)))
            .Select(mapping => $"{Where(mapping)} — .RequireAuthorization() names no scope")
            .ToArray();

        unregistered.ShouldBeEmpty(
            "these endpoints require authentication and nothing else: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, unregistered)}{Environment.NewLine}"
            + "Any authenticated caller reaches them, whatever they were granted. Enforce a scope from the "
            + "catalogue, or add the route to the register in this file against the issue that will — the "
            + "register is read in both directions, so the row fails the build again the day the scope "
            + "lands and must be deleted with it.");
    }

    /// <summary>
    /// <b>A7 — the register is read the other way too.</b>
    ///
    /// <para>
    /// The half that makes the register not an allow-list. A row whose endpoint
    /// now enforces a scope, or no longer exists, fails here — so fixing #2070
    /// cannot leave a stale row behind, quietly recording a problem nobody has.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_registered_route_still_enforces_no_scope()
    {
        EndpointMapping[] mappings = AllMappings();

        string[] stale = UnenforcedByDesign
            .Where(row => !mappings.Any(mapping => mapping.Kind == AuthorizationKind.Bare && Matches(row, mapping)))
            .Select(row => $"{row.Verb} {row.Route} (issue {row.Issue})")
            .ToArray();

        stale.ShouldBeEmpty(
            "these register rows no longer match an endpoint that enforces no scope: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, stale)}{Environment.NewLine}"
            + "Either the route was fixed — in which case delete the row, and let the scope sentence be "
            + "demanded of it like every other scoped endpoint — or it moved, in which case the row is "
            + "pointing at nothing and the endpoint it was meant to cover is being checked by neither "
            + "half. A register that is only read in one direction is an allow-list with better manners.");
    }

    /// <summary>
    /// <b>A8 — an anonymous endpoint says what authenticates it instead.</b>
    ///
    /// <para>
    /// Both of today's two are reached with a bearer the handler validates
    /// itself, and neither says so. <c>AllowAnonymous</c> in a chain reads as
    /// "no authentication" to anyone who does not open the handler.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_anonymous_endpoint_says_what_authenticates_it_instead()
    {
        string[] silent = AllMappings()
            .Where(mapping => mapping.Kind == AuthorizationKind.Anonymous)
            .Where(mapping => mapping.Summary is null
                || !mapping.Summary.Contains(AnonymousLabel, StringComparison.Ordinal))
            .Select(mapping => Anonymity(mapping))
            .ToArray();

        silent.ShouldBeEmpty(
            "these endpoints are explicitly anonymous and do not say why: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, silent)}{Environment.NewLine}"
            + "AllowAnonymous in a chain reads as unauthenticated to every reader who does not open the "
            + "handler, and both of these are in fact reached with a bearer the handler validates itself. "
            + "Say so in the summary, so the surface no longer looks like a hole.");
    }

    /// <summary>
    /// <b>A9 — the gate has no soft edge.</b>
    ///
    /// <para>
    /// <see cref="UnenforcedByDesign"/> is the one escape, and it is checked in
    /// both directions, cites an issue, and shows up in a diff. A second one
    /// added here — a list of files to skip, a rule to suppress — would be
    /// silent in every direction, and ADR-0144 names reaching green by weakening
    /// a gate as a blocked outcome rather than a judgement call.
    /// </para>
    ///
    /// <para>
    /// It reads code, not prose: comment lines, attribute lines and the content
    /// of string literals are outside the scan, because prose about this rule
    /// necessarily uses this rule's vocabulary and a scan that read it would
    /// turn a wording change red. It polices a vocabulary, not a mechanism —
    /// someone who names the same thing differently walks past it. That is a
    /// fair price for making the obvious move loud, and it is not a proof.
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
    public void The_guard_offers_no_way_to_excuse_an_endpoint(string mechanism)
    {
        string[] offenders = ExecutableLines(ReadRepositoryFile(GuardSource))
            .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the guard's own code names '{mechanism}': {string.Join(" | ", offenders)}. That reads as a "
            + "way to excuse an endpoint from the rule, and a rule with a soft edge is a review convention "
            + "wearing a build failure's clothes. The one escape is the register above, which is read in "
            + "both directions and cites an open issue; a second one that is read in neither is how a "
            + "convention goes quiet again.");
    }

    // ---- reading the chain -------------------------------------------------

    /// <summary>
    /// Every mapping in every endpoint file, in file order.
    /// </summary>
    private static EndpointMapping[] AllMappings() =>
        EndpointSourceFiles().SelectMany(file => Read(file).Mappings).ToArray();

    /// <summary>
    /// The scoped mappings of one file paired with the literal their constant
    /// resolves to. A constant that resolves to nothing is A3's to report, so it
    /// is left out here rather than failing twice for one cause.
    /// </summary>
    private static ScopedEndpoint[] ScopedWithLiteral(string file) =>
        Read(file).Mappings
            .Where(mapping => mapping.Kind == AuthorizationKind.Scoped)
            .Where(mapping => ScopeLiterals.ContainsKey(mapping.ScopeConstant))
            .Select(mapping => new ScopedEndpoint(mapping, ScopeLiterals[mapping.ScopeConstant]))
            .ToArray();

    /// <summary>
    /// Parses one endpoint file into its groups, its mappings and whatever it
    /// could not resolve. Comments are blanked before anything else, so a
    /// semicolon or a <c>Map*</c> sample inside one changes no result.
    /// </summary>
    private static EndpointFileReading Read(string file)
    {
        string text = WithoutComments(ReadRepositoryFile(file));
        string masked = MaskLiterals(text);

        Dictionary<string, RouteGroup> groups = new(StringComparer.Ordinal);
        List<(int From, int To)> declarations = [];
        List<string> unread = [];

        foreach (Match declaration in LocalDeclaration.Matches(masked))
        {
            int end = StatementEnd(masked, declaration.Index);
            string statement = masked[declaration.Index..end];
            if (!GroupSite.IsMatch(statement))
            {
                continue;
            }

            string name = declaration.Groups["name"].Value;
            declarations.Add((declaration.Index, end));
            groups[name] = new RouteGroup(
                name,
                FirstLiteral(text, masked, declaration.Index, end),
                DeclaredAuthorization(text, masked, declaration.Index, end));
        }

        foreach (Match site in GroupSite.Matches(masked))
        {
            if (!declarations.Any(span => span.From < site.Index && site.Index < span.To))
            {
                unread.Add($"{file}:{LineAt(text, site.Index)} — a MapGroup call outside a "
                    + "RouteGroupBuilder or var declaration this reader recognises, so no mapping can "
                    + "inherit the authorization it declares");
            }
        }

        List<EndpointMapping> mappings = [];
        foreach (Match site in MappingSite.Matches(masked))
        {
            EndpointMapping mapping = ReadMapping(file, text, masked, site, groups);
            mappings.Add(mapping);
            if (mapping.Problem is not null)
            {
                unread.Add($"{Where(mapping)} — {mapping.Problem}");
            }
        }

        return new EndpointFileReading(file, [.. mappings], [.. unread]);
    }

    private static EndpointMapping ReadMapping(
        string file,
        string text,
        string masked,
        Match site,
        Dictionary<string, RouteGroup> groups)
    {
        int start = site.Index;
        int end = StatementEnd(masked, start);
        string receiver = site.Groups["receiver"].Value;
        string verb = site.Groups["verb"].Value.ToUpperInvariant();

        int open = masked.IndexOf('(', site.Index + site.Length - 1);
        string route = FirstLiteralIn(text, masked, open, end);

        groups.TryGetValue(receiver, out RouteGroup? group);
        string fullRoute = JoinRoute(group?.Prefix ?? string.Empty, route);
        int line = LineAt(text, start);

        (string? summary, bool summaryUnreadable) = DeclaredSummary(text, masked, start, end);
        EnforcedAuthorization? own = DeclaredAuthorization(text, masked, start, end);
        EnforcedAuthorization effective = own ?? group?.Authorization
            ?? new EnforcedAuthorization(AuthorizationKind.Unreadable, ReceiverProblem(receiver, group));

        string? unreadableSummary = summaryUnreadable
            ? "its .WithSummary argument is not a plain string literal or a concatenation of them, so "
                + "the sentence it declares cannot be read"
            : null;

        string? problem = effective.Kind == AuthorizationKind.Unreadable
            ? effective.Detail
            : unreadableSummary;

        return new EndpointMapping(
            file,
            line,
            verb,
            fullRoute,
            effective.Kind,
            effective.Kind == AuthorizationKind.Scoped ? effective.Detail : string.Empty,
            summary,
            problem);
    }

    private static string ReceiverProblem(string receiver, RouteGroup? group) =>
        group is null
            ? $"its receiver '{receiver}' is not a route group declared in this file, so no group "
                + "authorization can be inherited and the chain declares none of its own"
            : "neither the chain nor its group declares any authorization, so the endpoint is "
                + "reachable without a token and says nothing about it";

    /// <summary>
    /// The authorization a chain declares, or <c>null</c> when it declares none
    /// and must inherit. An endpoint-level <c>RequireAuthorization</c> wins over
    /// the group's, which is how <c>SystemVariableEndpoints</c>' three writes
    /// override a group that names no scope.
    /// </summary>
    private static EnforcedAuthorization? DeclaredAuthorization(string text, string masked, int start, int end)
    {
        int require = IndexOfCall(masked, ".RequireAuthorization", start, end);
        if (require >= 0)
        {
            int open = masked.IndexOf('(', require);
            (int from, int to) = ArgumentSpan(masked, open);
            string argument = text[from..to].Trim();

            if (argument.Length == 0)
            {
                return new EnforcedAuthorization(AuthorizationKind.Bare, string.Empty);
            }

            return ConstantPath.IsMatch(argument)
                ? new EnforcedAuthorization(AuthorizationKind.Scoped, Compact(argument))
                : new EnforcedAuthorization(
                    AuthorizationKind.Unreadable,
                    $"its .RequireAuthorization argument '{argument}' is not a constant path, so the "
                        + "scope it enforces cannot be resolved");
        }

        return IndexOfCall(masked, ".AllowAnonymous", start, end) >= 0
            ? new EnforcedAuthorization(AuthorizationKind.Anonymous, string.Empty)
            : null;
    }

    /// <summary>
    /// The summary text a chain declares, and whether the declaration was
    /// readable. A missing <c>WithSummary</c> is <c>null</c> and readable; an
    /// interpolated one is unreadable rather than guessed at.
    /// </summary>
    private static (string? Summary, bool Unreadable) DeclaredSummary(
        string text,
        string masked,
        int start,
        int end)
    {
        int call = IndexOfCall(masked, ".WithSummary", start, end);
        if (call < 0)
        {
            return (null, false);
        }

        int open = masked.IndexOf('(', call);
        (int from, int to) = ArgumentSpan(masked, open);

        return TryJoinLiterals(text[from..to], out string joined) ? (joined, false) : (null, true);
    }

    /// <summary>
    /// The first string literal in a statement — a group's route prefix.
    /// </summary>
    private static string FirstLiteral(string text, string masked, int start, int end)
    {
        int call = IndexOfCall(masked, ".MapGroup", start, end);
        return call < 0 ? string.Empty : FirstLiteralIn(text, masked, masked.IndexOf('(', call), end);
    }

    private static string FirstLiteralIn(string text, string masked, int open, int end)
    {
        if (open < 0 || open >= end)
        {
            return string.Empty;
        }

        (int from, int to) = ArgumentSpan(masked, open);
        int quote = masked.IndexOf('"', from);

        if (quote < 0 || quote >= to)
        {
            return string.Empty;
        }

        int close = EndOfLiteral(masked, quote, verbatim: false);
        return Unescape(text[(quote + 1)..(close - 1)]);
    }

    private static string[] ClaimedScopes(string? summary) =>
        summary is null
            ? []
            : ClaimedScope.Matches(summary)
                .Select(match => match.Groups["claimed"].Value.TrimEnd('.'))
                .ToArray();

    private static bool Matches(UnenforcedRoute row, EndpointMapping mapping) =>
        string.Equals(row.Verb, mapping.Verb, StringComparison.Ordinal)
        && string.Equals(row.Route, mapping.Route, StringComparison.Ordinal);

    // ---- reporting ---------------------------------------------------------

    private static string Where(EndpointMapping mapping) =>
        $"{mapping.File}:{mapping.Line}  {mapping.Verb} {mapping.Route}";

    private static string Omission(EndpointMapping mapping, string literal) =>
        Where(mapping)
        + $"{Environment.NewLine}    enforces : {literal}  (via {mapping.ScopeConstant})"
        + $"{Environment.NewLine}    summary  : {Quoted(mapping.Summary)}"
        + $"{Environment.NewLine}    expected : the summary to contain {Quoted(ScopeLabel + literal)}";

    private static string Mismatch(EndpointMapping mapping, string literal, string claimed) =>
        Where(mapping)
        + $"{Environment.NewLine}    enforces : {literal}  (via {mapping.ScopeConstant})"
        + $"{Environment.NewLine}    claims   : {claimed}"
        + $"{Environment.NewLine}    summary  : {Quoted(mapping.Summary)}"
        + $"{Environment.NewLine}    expected : the summary to name {literal}, the scope the chain enforces";

    private static string Anonymity(EndpointMapping mapping) =>
        Where(mapping)
        + $"{Environment.NewLine}    enforces : nothing — .AllowAnonymous()"
        + $"{Environment.NewLine}    summary  : {Quoted(mapping.Summary)}"
        + $"{Environment.NewLine}    expected : the summary to contain {Quoted(AnonymousLabel)} and what "
        + "authenticates the call instead";

    private static string Quoted(string? value) =>
        value is null ? NoSummary : $"\"{value}\"";

    // ---- the scope catalogue, by reflection --------------------------------

    /// <summary>
    /// Every <c>public const string</c> on <see cref="Scope"/> and its nested
    /// classes, indexed by full constant path and by every tail of it that names
    /// exactly one literal. <c>Scope.All</c> is a property and is not walked —
    /// deliberately: it is a hand-maintained list, and a guard checked against
    /// it would agree with the list rather than with the constants the endpoints
    /// cite.
    /// </summary>
    private static Dictionary<string, string> BuildScopeCatalogue()
    {
        Dictionary<string, List<string>> candidates = new(StringComparer.Ordinal);
        Collect(typeof(Scope), nameof(Scope), candidates);

        return candidates
            .Where(entry => entry.Value.Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(entry => entry.Key, entry => entry.Value[0], StringComparer.Ordinal);
    }

    private static void Collect(Type type, string path, Dictionary<string, List<string>> candidates)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            string literal = (string)field.GetRawConstantValue()!;
            foreach (string key in Tails($"{path}.{field.Name}"))
            {
                if (!candidates.TryGetValue(key, out List<string>? found))
                {
                    found = [];
                    candidates[key] = found;
                }

                found.Add(literal);
            }
        }

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
        {
            Collect(nested, $"{path}.{nested.Name}", candidates);
        }
    }

    /// <summary>
    /// A constant path and every tail of it. Call sites write the full
    /// <c>Scope.Sse.…</c> form today; indexing the tails costs nothing and
    /// removes a false failure the first time someone shortens one with a
    /// <c>using static</c>. A tail that names two different literals is dropped
    /// by <see cref="BuildScopeCatalogue"/> rather than resolved arbitrarily.
    /// </summary>
    private static IEnumerable<string> Tails(string path)
    {
        string[] parts = path.Split('.');
        for (int index = 0; index < parts.Length; index++)
        {
            yield return string.Join('.', parts[index..]);
        }
    }

    // ---- source mechanics --------------------------------------------------

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/*/Api</c>, build output excluded. The
    /// API Gateway is not among them: it lives at <c>src/ApiGateway</c>, is pure
    /// YARP, and maps no route handler of its own.
    /// </summary>
    private static string[] ApiSourceFiles()
    {
        DirectoryInfo root = RepositoryRoot();

        return ContextApiDirectories()
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Select(file => RelativePath(root, file))
            .Where(IsSource)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] EndpointSourceFiles()
    {
        string[] files = ApiSourceFiles()
            .Where(path => path.EndsWith("Endpoints.cs", StringComparison.Ordinal))
            .ToArray();

        files.ShouldNotBeEmpty(
            $"no file matching {EndpointGlob} was found, so every assertion in this guard would pass "
            + "against an empty corpus. The endpoint files moved — point the sweep at where they now "
            + "live rather than leaving it reading nothing.");

        return files;
    }

    private static string[] ContextApiDirectories() =>
        Directory.EnumerateDirectories(Path.Combine(RepositoryRoot().FullName, "src"))
            .Select(context => Path.Combine(context, "Api"))
            .Where(Directory.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool IsSource(string relative) =>
        !relative.Contains("/obj/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal);

    /// <summary>
    /// Forward slashes throughout, and every path comparison and every report in
    /// this file is written against them. <see cref="Path.GetRelativePath"/>
    /// returns the <em>platform</em> separator, so a filter or an expected string
    /// written with a backslash is green on a Windows developer machine and red
    /// on Linux CI — the worst direction for a guard to break, because it passes
    /// exactly where nobody looks. This repository has been bitten by it.
    /// </summary>
    private static string RelativePath(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Line endings are stripped of <c>\r</c> on the way in, so every offset,
    /// line number and literal is the same on both platforms.
    /// </summary>
    private static string ReadRepositoryFile(string relativePath)
    {
        string path = Path.Combine(RepositoryRoot().FullName, relativePath);
        File.Exists(path).ShouldBeTrue(
            $"expected {relativePath} at {path} — if it moved, update this guard rather than deleting it.");
        return File.ReadAllText(path).Replace("\r", string.Empty, StringComparison.Ordinal);
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

    private static string Masked(string source) => MaskLiterals(WithoutComments(source));

    /// <summary>
    /// The source with every comment replaced by spaces, newlines kept, length
    /// preserved — so every offset still names the same line. A semicolon in a
    /// comment would otherwise end a statement early, and the <c>Map*</c> sample
    /// inside <c>RequireScopeExtensions</c>' own XML doc is exactly the shape
    /// that would be miscounted.
    /// </summary>
    private static string WithoutComments(string source)
    {
        char[] result = source.ToCharArray();
        int index = 0;

        while (index < source.Length)
        {
            char current = source[index];

            if (current == '/' && Next(source, index) == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    result[index++] = ' ';
                }

                continue;
            }

            if (current == '/' && Next(source, index) == '*')
            {
                while (index < source.Length && !(source[index] == '*' && Next(source, index) == '/'))
                {
                    if (source[index] != '\n')
                    {
                        result[index] = ' ';
                    }

                    index++;
                }

                for (int blank = 0; blank < 2 && index < source.Length; blank++)
                {
                    result[index++] = ' ';
                }

                continue;
            }

            if (current == '@' && Next(source, index) == '"')
            {
                index = EndOfLiteral(source, index + 1, verbatim: true);
                continue;
            }

            if (current is '"' or '\'')
            {
                index = EndOfLiteral(source, index, verbatim: false);
                continue;
            }

            index++;
        }

        return new string(result);
    }

    /// <summary>
    /// The same text with the <em>interior</em> of every literal blanked, quotes
    /// and length kept. Structural searches run on this; literal contents are
    /// then taken from the unmasked text at the very same offsets. It is what
    /// lets a summary contain a semicolon — several do — without ending the
    /// statement that carries it.
    /// </summary>
    private static string MaskLiterals(string text)
    {
        char[] result = text.ToCharArray();
        int index = 0;

        while (index < text.Length)
        {
            int start;
            int end;

            if (text[index] == '@' && Next(text, index) == '"')
            {
                start = index + 1;
                end = EndOfLiteral(text, index + 1, verbatim: true);
            }
            else if (text[index] is '"' or '\'')
            {
                start = index;
                end = EndOfLiteral(text, index, verbatim: false);
            }
            else
            {
                index++;
                continue;
            }

            for (int inner = start + 1; inner < end - 1 && inner < text.Length; inner++)
            {
                if (result[inner] != '\n')
                {
                    result[inner] = ' ';
                }
            }

            index = Math.Max(end, index + 1);
        }

        return new string(result);
    }

    private static char Next(string text, int index) =>
        index + 1 < text.Length ? text[index + 1] : '\0';

    private static int EndOfLiteral(string text, int start, bool verbatim)
    {
        char quote = text[start];
        int index = start + 1;

        while (index < text.Length)
        {
            char current = text[index];

            if (verbatim)
            {
                if (current == quote)
                {
                    if (Next(text, index) == quote)
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                index++;
                continue;
            }

            if (current == '\\')
            {
                index += 2;
                continue;
            }

            if (current == quote)
            {
                return index + 1;
            }

            if (current == '\n')
            {
                return index;
            }

            index++;
        }

        return index;
    }

    /// <summary>
    /// A chain runs from its <c>Map*</c> call to the terminating semicolon at
    /// statement level. Searched on the masked text, so a semicolon inside a
    /// summary does not end it early.
    /// </summary>
    private static int StatementEnd(string masked, int start)
    {
        int semicolon = masked.IndexOf(';', start);
        return semicolon < 0 ? masked.Length : semicolon;
    }

    private static int IndexOfCall(string masked, string call, int start, int end)
    {
        int found = masked.IndexOf(call, start, StringComparison.Ordinal);
        return found >= 0 && found < end ? found : -1;
    }

    /// <summary>
    /// The span between an opening parenthesis and its match, parentheses inside
    /// literals excluded because the literals are already blanked.
    /// </summary>
    private static (int From, int To) ArgumentSpan(string masked, int open)
    {
        int depth = 0;

        for (int index = open; index < masked.Length; index++)
        {
            if (masked[index] == '(')
            {
                depth++;
            }
            else if (masked[index] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return (open + 1, index);
                }
            }
        }

        return (open + 1, masked.Length);
    }

    /// <summary>
    /// Joins adjacent string literals and <c>+</c> concatenations into the one
    /// string the compiler would produce. Anything else in the argument —
    /// notably a <c>$</c> interpolation — leaves residue and the argument is
    /// reported unreadable rather than half-read. No summary interpolates today.
    /// </summary>
    private static bool TryJoinLiterals(string argument, out string joined)
    {
        StringBuilder value = new();
        StringBuilder residue = new();
        int index = 0;

        while (index < argument.Length)
        {
            if (argument[index] == '@' && Next(argument, index) == '"')
            {
                int end = EndOfLiteral(argument, index + 1, verbatim: true);
                value.Append(argument[(index + 2)..(end - 1)].Replace("\"\"", "\"", StringComparison.Ordinal));
                index = end;
                continue;
            }

            if (argument[index] == '"')
            {
                int end = EndOfLiteral(argument, index, verbatim: false);
                value.Append(Unescape(argument[(index + 1)..(end - 1)]));
                index = end;
                continue;
            }

            residue.Append(argument[index]);
            index++;
        }

        joined = value.ToString();

        return value.Length > 0
            && residue.ToString().All(character => char.IsWhiteSpace(character) || character == '+');
    }

    private static string Unescape(string literal) =>
        literal
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>
    /// A group prefix and a mapping route as one route, with the doubled and
    /// trailing slashes a naive join would leave behind removed.
    /// </summary>
    private static string JoinRoute(string prefix, string route)
    {
        string joined = $"{prefix.TrimEnd('/')}/{route.TrimStart('/')}";
        return joined.Length > 1 ? joined.TrimEnd('/') : joined;
    }

    private static string Compact(string path) =>
        new(path.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static int LineAt(string text, int index) =>
        text[..index].Count(character => character == '\n') + 1;

    /// <summary>
    /// Lines that are neither commentary nor attribute metadata, with the
    /// content of every string literal removed — what is left is the code that
    /// could carry a mechanism, rather than the prose it prints.
    /// </summary>
    private static IEnumerable<string> ExecutableLines(string source) =>
        source.Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith('['))
            .Select(line => StringLiteral.Replace(line, "\"\""));

    private enum AuthorizationKind
    {
        Scoped,
        Anonymous,
        Bare,
        Unreadable,
    }

    /// <summary>
    /// A route that enforces no scope, and the open issue that will give it one.
    /// </summary>
    private sealed record UnenforcedRoute(string Verb, string Route, int Issue);

    /// <summary>
    /// The authorization a chain declares. <see cref="Detail"/> carries the
    /// constant path when scoped, and the reason when unreadable.
    /// </summary>
    private sealed record EnforcedAuthorization(AuthorizationKind Kind, string Detail);

    private sealed record RouteGroup(string Name, string Prefix, EnforcedAuthorization? Authorization);

    /// <summary>
    /// One route-handler registration. <see cref="Problem"/> is the reason the
    /// reader could not resolve it, and is <c>null</c> when it could.
    /// </summary>
    private sealed record EndpointMapping(
        string File,
        int Line,
        string Verb,
        string Route,
        AuthorizationKind Kind,
        string ScopeConstant,
        string? Summary,
        string? Problem);

    private sealed record EndpointFileReading(string File, EndpointMapping[] Mappings, string[] Unread);

    private sealed record ScopedEndpoint(EndpointMapping Mapping, string Literal);
}
