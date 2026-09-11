using System.Text.RegularExpressions;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Persistence;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Draws the line this repository has crossed six times without naming it
/// (issue #2142, spec 130): <b>a status produced somewhere other than the line
/// that maps the route does not get declared</b>.
///
/// <para>
/// The endpoint author declares what they wrote. The request also meets
/// middleware, an authorization policy, a registered exception handler, a helper
/// the handler calls — none of them visible at the <c>Map…</c> chain. The
/// generated OpenAPI then asserts a status the route certainly returns cannot
/// happen, and a generated client has no branch for it. Six instances were found
/// one at a time, from six directions: #91, #2088/spec 072, #2101, #2096/spec
/// 075, #2113/spec 085, #2114/spec 091.
/// </para>
///
/// <para>
/// <b>The line, stated.</b> A mechanism is <em>derivable</em> when its antecedent
/// is reachable from the mapping without leaving the Api project, and the census
/// below splits every mechanism in this tree three ways:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Chain-visible</b> — the antecedent is a call in the mapping's own fluent
/// chain, or in the <c>MapGroup</c> it was written on. One guard, every route,
/// failing on code nobody has written yet. Two mechanisms: the 403 a scope
/// policy produces (derived by <c>EndpointScopeDeclarationTests</c> since spec
/// 085) and the <b>401 the authentication behind it produces</b> — derived here,
/// by nothing before.
/// </item>
/// <item>
/// <b>Handler-body visible</b> — one hop: resolve the method group the chain
/// names and read its body. Demonstrated by <c>PreconditionDeclarationTests</c>
/// (spec 072) and <c>RouteValueRefusalDeclarationTests</c> (spec 091). This file
/// adds no fourth.
/// </item>
/// <item>
/// <b>Not visible</b> — the producer is in Application, in a shared exception
/// handler, or in another process. <b>A register is the ceiling</b>, which is
/// what <c>ConcurrencyConflictDeclarationTests</c> is, and why spec 075 said so
/// in its own doc rather than presenting a typed classification as a rule.
/// </item>
/// </list>
///
/// <para>
/// <b>The gap, measured on this branch before the change.</b> 56 route-handler
/// mappings in 12 <c>*Endpoints.cs</c> files; 54 authorized, 2
/// <c>AllowAnonymous</c>. Exactly two <c>Status401Unauthorized</c> declarations
/// existed anywhere under <c>src/*/Api</c>, and <em>both sat on the two anonymous
/// chains</em>, where the handler validates a bearer itself. So <b>0 of 54</b>
/// authorized mappings declared the challenge their own
/// <c>RequireAuthorization</c> produces, while 54 of 54 declared the 403 from
/// the same antecedent — the asymmetry that shows #2113 closed an instance and
/// not a class.
/// </para>
///
/// <para>
/// <b>The antecedent, stated exactly.</b> Within the mapping's own chain span —
/// from the <c>.MapX(</c> call to the statement's terminating semicolon, comments
/// blanked and literal <em>content</em> masked — or within the chain of the
/// <c>MapGroup</c> statement whose bound variable is the mapping's receiver: a
/// call matching <c>\.RequireAuthorization\s*\(</c>, with no
/// <c>\.AllowAnonymous\s*\(</c> in the mapping's own chain. Anonymity on the
/// mapping beats authorization on the group, which is ASP.NET's own precedence
/// and how both of today's anonymous routes are written. <b>The consequent:</b>
/// within the mapping's own chain,
/// <c>\.Produces(?:Validation)?Problem\(\s*StatusCodes\.Status401Unauthorized\s*\)</c>
/// — both spellings, because <c>PreconditionDeclarationTests</c> learned in spec
/// 072 that matching only the first reports correct code as broken.
/// </para>
///
/// <para>
/// <b>The rule runs one way only.</b> A mirror rule — <em>declares 401 ⇒ must
/// require authorization</em> — would fail the two anonymous routes on its first
/// run, and they are correct: their handlers answer 401 for a bearer they reject
/// themselves. There is deliberately none.
/// </para>
///
/// <para>
/// <b>Why a new file.</b> <c>EndpointScopeDeclarationTests</c> is the nearest
/// neighbour by antecedent and is 2181 lines; its subject is the scope an
/// endpoint names, and this rule binds bare <c>RequireAuthorization()</c> too.
/// The census belongs to neither existing file, because it is about all four
/// guards at once. The named cost is a sixth copy of <c>RepositoryRoot()</c>, the
/// masker and the chain reader; the extraction spec 091 assigned to #2142 is a
/// behaviour-preserving refactor and cannot ride with a behaviour-changing
/// change (ADR-0144), so it needs its own issue and is recorded in
/// <c>specs/130-…/tasks.md</c> rather than done here.
/// </para>
/// </summary>
public class StatusProducerDeclarationTests
{
    private const string GuardSource = "tests/Architecture.Tests/StatusProducerDeclarationTests.cs";
    private const string EndpointFileSuffix = "Endpoints.cs";

    /// <summary>Every <c>.Map(Get|Post|Put|Patch|Delete)(</c> site under <c>src/*/Api</c>.</summary>
    private const int RouteHandlerMappingCount = 56;

    /// <summary>Every file under <c>src/*/Api</c> whose name ends <c>Endpoints.cs</c>.</summary>
    private const int EndpointFileCount = 12;

    /// <summary>Where the exception handlers this census classifies are registered.</summary>
    private const string RegistrationSource = "src/ServiceDefaults/AuthenticationDefaults.cs";

    private static readonly Regex MappingCall = new(
        @"(?<receiver>[A-Za-z_]\w*)\.Map(?<verb>Get|Post|Put|Patch|Delete)\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex GroupDeclaration = new(
        @"(?<variable>[A-Za-z_]\w*)\s*=\s*[A-Za-z_]\w*\s*\.MapGroup\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Authorization = new(
        @"\.RequireAuthorization\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Anonymity = new(
        @"\.AllowAnonymous\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex ChallengeDeclaration = new(
        @"\.Produces(?:Validation)?Problem\(\s*StatusCodes\.Status401Unauthorized\s*\)",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DeclaredStatus = new(
        @"StatusCodes\.Status(?<code>\d{3})[A-Za-z]*",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Lazy<Surface> TheSurface = new(Read);

    /// <summary>
    /// The census: every mechanism in this tree that can put a status on a
    /// response the mapping line does not name.
    ///
    /// <para>
    /// <b>This is a register where it is a classification, and it says so.</b>
    /// The <em>population</em> of registered exception handlers is derived by
    /// reflection in <see cref="Every_registered_exception_handler_is_classified_by_the_census"/>,
    /// so a sixth one fails the build until someone places it. Which side of the
    /// line each mechanism falls on is typed in. A green run proves the five
    /// known handlers still exist, are still registered and are still placed —
    /// it cannot notice a mechanism nobody wrote down. The rate limiter below
    /// was found by sweeping for <c>RateLimiter</c>, not by any test.
    /// </para>
    /// </summary>
    private static readonly Mechanism[] Census =
    [
        new("M1", "JWT bearer challenge on an authorized route", "401", Visibility.Chain,
            GuardSource, nameof(Every_authorized_mapping_declares_the_challenge_its_authorization_produces)),
        new("M2", "AddScopePolicies claim assertion", "403", Visibility.Chain,
            "tests/Architecture.Tests/EndpointScopeDeclarationTests.cs",
            "Every_scoped_endpoint_declares_the_refusal_its_scope_produces"),
        new("M3", nameof(BadHttpRequestExceptionHandler), "400/413/415", Visibility.HandlerBody, null, null),
        new("M4", nameof(FabAuthorizationExceptionHandler), "403", Visibility.None, null, null),
        new("M5", nameof(UnattributableOperatorExceptionHandler), "401", Visibility.None, null, null),
        new("M6", nameof(ConcurrencyConflictExceptionHandler), "409", Visibility.None, null, null),
        new("M7", nameof(UniqueConstraintExceptionHandler), "409", Visibility.None, null, null),
        new("M8", "ApiError.Status rendering a Result failure", "as carried", Visibility.None, null, null),
        new("M9", "IdempotentRequest in-progress refusal", "409", Visibility.HandlerBody, null, null),
        new("M10", "IdempotencyHeaders.TryRead malformed key", "400", Visibility.HandlerBody, null, null),
        new("M11", "ConcurrencyHeaders If-Match read", "428/400", Visibility.HandlerBody,
            "tests/Architecture.Tests/PreconditionDeclarationTests.cs",
            "Every_endpoint_that_requires_If_Match_declares_the_428_it_answers"),
        new("M12", "the handler's own route-value refusal", "400", Visibility.HandlerBody,
            "tests/Architecture.Tests/RouteValueRefusalDeclarationTests.cs",
            "Every_route_whose_handler_can_answer_400_declares_it_on_its_own_chain"),
        new("M13", "route-constraint mismatch", "404", Visibility.Chain, null, null),
        new("M14", "ApiGateway fixed-window rate limiter", "429", Visibility.None, null, null),
    ];

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

    // ---- L1: the claim -----------------------------------------------------

    /// <summary>
    /// <b>L1 — a mapping that requires authorization declares the challenge that
    /// authorization produces.</b>
    ///
    /// <para>
    /// Neither the antecedent nor the population is typed in: authorization is
    /// resolved from the chain, or from the <c>MapGroup</c> the mapping was
    /// written on, and 28 of the 54 are in this population only through that
    /// inheritance. Delete a route's <c>RequireAuthorization</c> and it leaves in
    /// the same edit — which is what makes this a derivation rather than a list
    /// of routes wearing a build failure's clothes.
    /// </para>
    ///
    /// <para>
    /// One case per endpoint file, so a failure names the file rather than a
    /// total: a single aggregate lets one file stop being read while the others
    /// carry the count.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointFiles))]
    public void Every_authorized_mapping_declares_the_challenge_its_authorization_produces(string file)
    {
        RouteMapping[] inFile = TheSurface.Value.Routes
            .Where(mapping => string.Equals(mapping.File, file, StringComparison.Ordinal))
            .ToArray();

        inFile.Length.ShouldBeGreaterThan(
            0,
            $"'{file}' is named like an endpoint file and yielded no route mapping this guard can read. "
            + "Either it stopped mapping routes — rename it — or it maps them in a shape the reader does "
            + "not parse, in which case none of its endpoints are checked at all and this assertion would "
            + "pass over an empty set.");

        string[] undeclared = inFile
            .Where(mapping => mapping.Kind == AccessKind.Authorized)
            .Where(mapping => !ChallengeDeclaration.IsMatch(mapping.Chain))
            .Select(Describe)
            .ToArray();

        undeclared.ShouldBeEmpty(
            "these mappings require authorization and never declare the challenge it produces: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, undeclared)}{Environment.NewLine}"
            + "AddBearerAuthentication registers JwtBearer and AddScopePolicies builds every sse.* policy "
            + "as RequireAuthenticatedUser() plus a claim assertion, so a caller with no token, an expired "
            + "one or one from the wrong issuer is challenged with 401 — not forbidden, which is the 403 "
            + "these same chains already declare. The document tells a client author that answer cannot "
            + "happen, and a generated client has no branch for it. Add "
            + ".ProducesProblem(StatusCodes.Status401Unauthorized) to the mapping's own chain; change no "
            + "route, scope, summary or handler.");
    }

    // ---- L2..L5: what keeps L1 sound ---------------------------------------

    /// <summary>
    /// <b>L2 — no route group declares the challenge its members must declare.</b>
    ///
    /// <para>
    /// Not a style rule: OpenAPI <em>inherits</em> <c>MapGroup</c> metadata, so a
    /// 401 on a group would make every operation under it correct while L1 still
    /// demanded a per-chain line from each — a guard failing correct code.
    /// Failing on the group instead says the shape is refused and why, rather
    /// than accusing eight endpoints of an omission they do not have.
    /// </para>
    /// </summary>
    [Fact]
    public void No_route_group_declares_the_challenge_its_members_must_declare()
    {
        string[] declaring = TheSurface.Value.Groups
            .Where(group => ChallengeDeclaration.IsMatch(group.Chain))
            .Select(group => $"{group.File}:{group.Line}  MapGroup(\"{group.Prefix}\") on '{group.Variable}'")
            .ToArray();

        declaring.ShouldBeEmpty(
            "these route groups declare the 401 their members are required to declare: "
            + $"{Environment.NewLine}{string.Join(Environment.NewLine, declaring)}{Environment.NewLine}"
            + "The resulting document would be correct — OpenAPI inherits group metadata — and that is the "
            + "problem: L1 reads each mapping's own chain, so every endpoint under this group is then "
            + "reported as omitting a declaration it effectively has, and every endpoint added under it "
            + "later inherits a declaration nobody wrote for it. One shape or the other, not both.");
    }

    /// <summary>
    /// <b>L3 — every challenge declaration under these directories is one the
    /// walk can see.</b>
    ///
    /// <para>
    /// Without it L1's sharpest edge is silent: a declaration hoisted into a
    /// shared convention, an endpoint filter or a metadata helper reads to the
    /// walk as no declaration at all, and L1 would then report an omission that
    /// is not there. The property is <c>PaginatedConsumerTests</c>', borrowed by
    /// <c>PreconditionDeclarationTests</c>, by <c>EndpointScopeDeclarationTests</c>,
    /// and again here.
    /// </para>
    ///
    /// <para>
    /// <b>Its completeness stops at the <c>src/*/Api</c> root</b>, and that is
    /// worth saying rather than implying. A helper inside that root is caught —
    /// the sweep reads every <c>.cs</c> file there. A convention added to
    /// <c>src/ServiceDefaults</c> removes the declaration from both counts
    /// equally, so they still agree and this stays green; only L1 fires, saying
    /// the endpoint declares nothing — true of the text it can see, and
    /// misleading about the document.
    /// </para>
    /// </summary>
    [Fact]
    public void The_challenge_declarations_the_walk_finds_are_all_the_ones_there_are()
    {
        int swept = TheSurface.Value.Masked.Values.Sum(text => ChallengeDeclaration.Count(text));
        int walked = TheSurface.Value.Routes.Sum(mapping => ChallengeDeclaration.Count(mapping.Chain))
            + TheSurface.Value.Groups.Sum(group => ChallengeDeclaration.Count(group.Chain));

        swept.ShouldBeGreaterThan(
            0,
            "no Status401Unauthorized declaration was found anywhere under src/*/Api. Two chains declared "
            + "one before spec 130 and fifty-six after, so zero means the sweep is reading nothing — most "
            + "likely the declaration is now spelled some way this pattern does not match, in which case "
            + "L1 is green because it checked no text at all.");

        walked.ShouldBe(
            swept,
            $"the walk found {walked} Status401Unauthorized declarations inside mapping and group chains; a "
            + $"flat sweep of src/*/Api found {swept}. A declaration the walk cannot see is one this guard "
            + "does not credit: it sits outside the fluent chain the reader captures — in a shared "
            + "convention, an endpoint filter, a metadata helper — and L1 would report its endpoint as "
            + "declaring nothing while the document is in fact correct. Put it in the mapping's own chain, "
            + "or teach the reader the shape.");
    }

    /// <summary>
    /// <b>L4 — the corpus is the one this guard thinks it is.</b>
    ///
    /// <para>
    /// Both numbers are computed from the same directories, so a file moved out
    /// of <c>src/*/Api</c> shrinks both together and every endpoint in it goes
    /// silently unguarded. The pinned counts are what see that; the agreement is
    /// what sees a mapping written in a shape the reader cannot parse.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_route_handler_mapping_under_the_api_projects_is_enumerated()
    {
        Surface surface = TheSurface.Value;
        int swept = surface.Masked.Values.Sum(text => MappingCall.Count(text));

        surface.EndpointSources.Count.ShouldBe(
            EndpointFileCount,
            $"src/*/Api/**/*Endpoints.cs matched {surface.EndpointSources.Count} files, not the "
            + $"{EndpointFileCount} this guard is pinned to:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, surface.EndpointSources)}{Environment.NewLine}"
            + "Fewer means a file left the sweep and every endpoint in it is now unchecked on a green "
            + "build. More means a new endpoint file the counts have not been told about. Either way, edit "
            + "the number here in the same diff as the file.");

        swept.ShouldBe(
            RouteHandlerMappingCount,
            $"the sweep over src/*/Api found {swept} route handlers, not the {RouteHandlerMappingCount} "
            + "this guard is pinned to. An endpoint was added, removed or moved out of the swept "
            + "directories; the last of those is invisible to every other assertion here.");

        surface.Routes.Count.ShouldBe(
            swept,
            $"src/*/Api registers {swept} route handlers, but the walk enumerated {surface.Routes.Count}. "
            + "A mapping this guard never enumerates is one it never checks, and it fails nothing on its "
            + "way past.");

        string[] outside = surface.Routes
            .Select(mapping => mapping.File)
            .Distinct(StringComparer.Ordinal)
            .Where(file => !surface.EndpointSources.Contains(file, StringComparer.Ordinal))
            .ToArray();

        outside.ShouldBeEmpty(
            $"these files declare a route mapping and do not end '{EndpointFileSuffix}':{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, outside)}{Environment.NewLine}"
            + "L1 is driven by EndpointFiles(), named only from files with that suffix, so a mapping "
            + "declared anywhere else is enumerated here and never handed to the rule — it would pass over "
            + "it unnoticed. Move the mapping into an *Endpoints.cs file, or rename the file.");
    }

    /// <summary>
    /// <b>L5 — every mapping is classified, and the antecedent is a filter
    /// rather than a tautology.</b>
    ///
    /// <para>
    /// This is the assertion that stops L1 passing by reading nothing. If
    /// <c>RequireAuthorization</c> is renamed, spelled through a helper, or the
    /// reader stops binding a mapping to its group, the 54 do not become
    /// offenders — they become <em>unclassified</em>, and L1 goes green over an
    /// empty population. Here they fail instead, naming the count.
    /// </para>
    ///
    /// <para>
    /// The anonymous half is asserted non-empty for the opposite reason: a rule
    /// whose antecedent is satisfied by every member of its corpus is not a
    /// filter, and nobody would notice it had stopped being one.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_mapping_is_either_authorized_or_explicitly_anonymous()
    {
        RouteMapping[] mappings = TheSurface.Value.Routes.ToArray();

        string[] unclassified = mappings
            .Where(mapping => mapping.Kind == AccessKind.Unclassified)
            .Select(Describe)
            .ToArray();

        unclassified.ShouldBeEmpty(
            "these mappings neither require authorization nor allow anonymity, on their own chain or on "
            + $"the group they were written on:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, unclassified)}{Environment.NewLine}"
            + "Either the endpoint really is open — in which case say so with .AllowAnonymous() and a "
            + "summary that names what authenticates it instead, as EndpointScopeDeclarationTests demands "
            + "— or the reader has stopped seeing an authorization call it used to see, in which case L1 "
            + "above is green over a population that has quietly emptied.");

        mappings.Count(mapping => mapping.Kind == AccessKind.Authorized).ShouldBeGreaterThan(
            0,
            "no mapping resolved as authorized, so L1 checked nothing. Fifty-four did before spec 130.");

        mappings.Count(mapping => mapping.Kind == AccessKind.Anonymous).ShouldBeGreaterThan(
            0,
            "no mapping resolved as anonymous, so L1's antecedent now admits every route in the corpus. "
            + "Two did before spec 130 — the webhook ingest and the WHEP hook — and a rule that filters "
            + "nothing is one nobody notices has stopped filtering.");
    }

    // ---- L6..L7: the line itself, made load-bearing -------------------------

    /// <summary>
    /// <b>L6 — every registered exception handler is classified by the census.</b>
    ///
    /// <para>
    /// The population is derived: reflection over the ServiceDefaults assembly
    /// for every type implementing <c>IExceptionHandler</c>. A sixth one fails
    /// here until someone says which side of the line it falls on — which is the
    /// point of the census, since a shared exception handler is the archetype of
    /// a producer no route can see. Registration is checked too, so a handler
    /// that exists and is never wired is not silently classified as a producer.
    /// </para>
    ///
    /// <para>
    /// Read both ways: a census row naming a handler that no longer exists fails
    /// as well, so the table cannot keep recording a mechanism the tree has lost.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_registered_exception_handler_is_classified_by_the_census()
    {
        string[] handlers = typeof(Scope).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(face =>
                string.Equals(face.Name, "IExceptionHandler", StringComparison.Ordinal)))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        handlers.Length.ShouldBeGreaterThan(
            0,
            "reflection over the ServiceDefaults assembly found no IExceptionHandler at all. Five were "
            + "registered when this census was written, and every row below claiming one would then be "
            + "checked against nothing.");

        string registrations = ReadRepositoryFile(RegistrationSource);

        string[] unclassified = handlers
            .Where(handler => !Census.Any(row => row.Name.Contains(handler, StringComparison.Ordinal)))
            .Select(handler => $"{handler} — implements IExceptionHandler and is in no census row")
            .ToArray();

        unclassified.ShouldBeEmpty(
            $"these exception handlers are not classified by the census:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, unclassified)}{Environment.NewLine}"
            + "An exception handler writes a status onto a response no mapping line mentions — the "
            + "archetype of this defect class. Add a row saying which status it produces and whether its "
            + "antecedent is visible from the mapping. If it is not, the row records that a register is "
            + "the ceiling; that is an answer, not a gap.");

        string[] unregistered = handlers
            .Where(handler => !registrations.Contains($"AddExceptionHandler<{handler}>", StringComparison.Ordinal))
            .Select(handler => $"{handler} — no AddExceptionHandler<{handler}>() in {RegistrationSource}")
            .ToArray();

        unregistered.ShouldBeEmpty(
            $"these exception handlers exist and are never registered:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, unregistered)}{Environment.NewLine}"
            + "The census records them as producers of a status the document must declare. One that runs "
            + "nowhere produces nothing, and the row is then a claim about behaviour the tree does not "
            + "have.");

        string[] stale = Census
            .Where(row => row.Name.EndsWith("ExceptionHandler", StringComparison.Ordinal))
            .Where(row => !handlers.Contains(row.Name, StringComparer.Ordinal))
            .Select(row => $"{row.Identifier} — {row.Name} is in the census and implements no IExceptionHandler")
            .ToArray();

        stale.ShouldBeEmpty(
            $"these census rows name an exception handler the tree no longer has:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, stale)}{Environment.NewLine}"
            + "A census read in one direction is a list that outlives what it describes. Delete the row "
            + "with the handler.");
    }

    /// <summary>
    /// <b>L7 — a mechanism the census calls derivable names the guard that
    /// derives it, and that guard exists.</b>
    ///
    /// <para>
    /// This is what stops the line from being prose. The claim "M2 is derived"
    /// is worth nothing if A13 can be deleted or renamed without anything
    /// noticing; here the census row fails with it. Rows with no named guard are
    /// left alone deliberately — M13's routing 404 has no operation to attach a
    /// response to, and the handler-body band is only partly covered, which the
    /// spec records rather than hides.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_mechanism_the_census_calls_derived_names_a_guard_that_exists()
    {
        string[] claimed = Census
            .Where(row => row.Guard is not null && row.GuardMethod is not null)
            .Select(row => $"{row.Identifier} -> {row.Guard}#{row.GuardMethod}")
            .ToArray();

        claimed.Length.ShouldBeGreaterThan(
            2,
            "the census claims fewer than three derived mechanisms. Four were claimed when it was "
            + "written — the 401 here, the 403 in spec 085, the 428 in spec 072 and the 400 in spec 091 — "
            + "so a smaller number means rows lost their guard reference and this assertion now checks "
            + "almost nothing.");

        string[] missing = Census
            .Where(row => row.Guard is not null && row.GuardMethod is not null)
            .Where(row => !ReadRepositoryFile(row.Guard!)
                .Contains($"public void {row.GuardMethod}(", StringComparison.Ordinal))
            .Select(row => $"{row.Identifier} ({row.Status}) — {row.Guard} declares no {row.GuardMethod}(")
            .ToArray();

        missing.ShouldBeEmpty(
            $"these census rows claim a derivation that is not there:{Environment.NewLine}"
            + $"{string.Join(Environment.NewLine, missing)}{Environment.NewLine}"
            + "The guard was renamed, moved or deleted, and the census went on recording the mechanism as "
            + "covered. A line drawn in prose that nothing checks is how this defect class stayed "
            + "findable six separate times.");

        Census.Where(row => row.Visibility == Visibility.Chain)
            .Select(row => row.Identifier)
            .ShouldContain(
                "M1",
                "the chain-visible band no longer contains M1, the 401 this file derives. The band is the "
                + "line's sharp side: if it empties, the guard has stopped claiming anything.");
    }

    /// <summary>
    /// <b>L8 — the gate has no soft edge.</b>
    ///
    /// <para>
    /// ADR-0144 names reaching green by weakening a gate as a blocked outcome
    /// rather than a judgement call. This reads code, not prose: comment lines
    /// and literal content are outside the scan, because prose about this rule
    /// necessarily uses this rule's vocabulary. It polices a vocabulary, not a
    /// mechanism — someone who names the same thing differently walks past it.
    /// That is a fair price for making the obvious move loud, and it is not a
    /// proof.
    /// </para>
    /// </summary>
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
        string[] offenders = Mask(ReadRepositoryFile(GuardSource))
            .Split('\n')
            .Where(line => line.Contains(mechanism, StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .ToArray();

        offenders.ShouldBeEmpty(
            $"the guard's own code names '{mechanism}': {string.Join(" | ", offenders)}. That reads as a "
            + "way to excuse an endpoint from the rule, and a rule with a soft edge is a review convention "
            + "wearing a build failure's clothes.");
    }

    // ---- reading the surface -----------------------------------------------

    private static string Describe(RouteMapping mapping)
    {
        string[] declared = DeclaredStatus.Matches(mapping.Chain)
            .Select(match => match.Groups["code"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string states = declared.Length == 0 ? "no status at all" : string.Join(", ", declared);

        return $"{mapping.File}:{mapping.Line} {mapping.Verb} {mapping.FullRoute} declares {states}";
    }

    private static Surface Read()
    {
        DirectoryInfo root = RepositoryRoot();
        List<string> files = ApiSourceFiles(root);

        Dictionary<string, string> masked = new(StringComparer.Ordinal);
        Dictionary<string, string> text = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string body = File.ReadAllText(Path.Combine(root.FullName, file))
                .Replace("\r", string.Empty, StringComparison.Ordinal);
            text[file] = body;
            masked[file] = Mask(body);
        }

        List<RouteGroup> groups = files.SelectMany(file => RouteGroups(file, text[file], masked[file])).ToList();
        List<RouteMapping> mappings = files
            .SelectMany(file => Mappings(file, text[file], masked[file], groups))
            .ToList();

        return new Surface(
            files.Where(file => file.EndsWith(EndpointFileSuffix, StringComparison.Ordinal)).ToList(),
            masked,
            groups,
            mappings);
    }

    private static List<string> ApiSourceFiles(DirectoryInfo root)
    {
        string src = Path.Combine(root.FullName, "src");
        return Directory.EnumerateDirectories(src)
            .Select(context => Path.Combine(context, "Api"))
            .Where(Directory.Exists)
            .SelectMany(api => Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
            .Select(file => Relative(root, file))
            .Where(file => !file.Contains("/obj/", StringComparison.Ordinal)
                && !file.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every route group in one file: the variable it is bound to, its prefix
    /// literal, and the whole statement as its chain. The prefix is read from the
    /// unmasked text, the structure from the masked.
    /// </summary>
    private static IEnumerable<RouteGroup> RouteGroups(string file, string text, string masked)
    {
        foreach (Match declaration in GroupDeclaration.Matches(masked))
        {
            int open = declaration.Index + declaration.Length - 1;
            int close = Balanced(masked, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            int end = StatementEnd(masked, close + 1);
            List<(int Start, int End)> arguments = SplitArguments(masked, open + 1, close);
            string prefix = arguments.Count > 0
                ? Unquote(text[arguments[0].Start..arguments[0].End].Trim())
                : string.Empty;

            yield return new RouteGroup(
                file,
                LineOf(masked, declaration.Index),
                declaration.Groups["variable"].Value,
                prefix,
                end < 0 ? masked[declaration.Index..] : masked[declaration.Index..end]);
        }
    }

    /// <summary>
    /// Every <c>Map*</c> call in one file: the route literal and the whole fluent
    /// chain to its terminating semicolon, captured from the masked text so a
    /// status named inside a summary is prose rather than a declaration.
    /// </summary>
    private static IEnumerable<RouteMapping> Mappings(
        string file,
        string text,
        string masked,
        IReadOnlyList<RouteGroup> groups)
    {
        foreach (Match call in MappingCall.Matches(masked))
        {
            int open = call.Index + call.Length - 1;
            int close = Balanced(masked, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            int end = StatementEnd(masked, close + 1);
            string chain = end < 0 ? masked[call.Index..] : masked[call.Index..end];

            List<(int Start, int End)> arguments = SplitArguments(masked, open + 1, close);
            string route = arguments.Count > 0
                ? Unquote(text[arguments[0].Start..arguments[0].End].Trim())
                : string.Empty;

            RouteGroup? group = groups.FirstOrDefault(candidate =>
                string.Equals(candidate.File, file, StringComparison.Ordinal)
                && string.Equals(candidate.Variable, call.Groups["receiver"].Value, StringComparison.Ordinal));

            yield return new RouteMapping(
                file,
                LineOf(masked, call.Index),
                call.Groups["verb"].Value.ToUpperInvariant(),
                route,
                group?.Prefix ?? string.Empty,
                chain,
                Classify(chain, group));
        }
    }

    /// <summary>
    /// The access a mapping resolves to. Anonymity on the mapping's own chain
    /// beats authorization on the group it was written on — ASP.NET's own
    /// precedence, and how both of today's anonymous routes are written.
    /// </summary>
    private static AccessKind Classify(string chain, RouteGroup? group)
    {
        if (Anonymity.IsMatch(chain))
        {
            return AccessKind.Anonymous;
        }

        if (Authorization.IsMatch(chain))
        {
            return AccessKind.Authorized;
        }

        if (group is null)
        {
            return AccessKind.Unclassified;
        }

        if (Anonymity.IsMatch(group.Chain))
        {
            return AccessKind.Anonymous;
        }

        return Authorization.IsMatch(group.Chain) ? AccessKind.Authorized : AccessKind.Unclassified;
    }

    /// <summary>
    /// The index of the semicolon that ends the statement starting at
    /// <paramref name="from"/>, ignoring semicolons nested inside brackets.
    /// </summary>
    private static int StatementEnd(string masked, int from)
    {
        int depth = 0;
        for (int i = from; i < masked.Length; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ';' && depth <= 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The half-open spans of the top-level arguments.</summary>
    private static List<(int Start, int End)> SplitArguments(string masked, int from, int close)
    {
        List<(int Start, int End)> arguments = [];
        int depth = 0;
        int start = from;
        for (int i = from; i < close; i++)
        {
            char c = masked[i];
            if (c is '(' or '[' or '{' or '<')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}' or '>')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                arguments.Add((start, i));
                start = i + 1;
            }
        }

        if (close > start)
        {
            arguments.Add((start, close));
        }

        return arguments;
    }

    private static string Unquote(string value) =>
        value.Length > 1 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>The index of the delimiter matching the one at <paramref name="openIndex"/>, or -1.</summary>
    private static int Balanced(string text, int openIndex, char open, char close)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// The source with comment and literal <em>content</em> replaced by spaces,
    /// the same length throughout so every index still points at the same
    /// character of the original. Delimiters are kept, so a route literal is
    /// still recognisable as one.
    /// </summary>
    private static string Mask(string text)
    {
        char[] masked = text.ToCharArray();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && Next(text, i) == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    masked[i++] = ' ';
                }
            }
            else if (text[i] == '/' && Next(text, i) == '*')
            {
                i = MaskBlockComment(text, masked, i);
            }
            else if (text[i] == '@' && Next(text, i) == '"')
            {
                i = MaskVerbatim(text, masked, i);
            }
            else if (text[i] is '"' or '\'')
            {
                i = MaskLiteral(text, masked, i);
            }
            else
            {
                i++;
            }
        }

        return new string(masked);
    }

    private static int MaskBlockComment(string text, char[] masked, int from)
    {
        int i = from;
        while (i < text.Length && !(text[i] == '*' && Next(text, i) == '/'))
        {
            masked[i] = text[i] == '\n' ? '\n' : ' ';
            i++;
        }

        return Blank(masked, i, 2);
    }

    private static int MaskVerbatim(string text, char[] masked, int from)
    {
        int i = from + 2;
        while (i < text.Length)
        {
            if (text[i] == '"' && Next(text, i) == '"')
            {
                masked[i] = ' ';
                masked[i + 1] = ' ';
                i += 2;
                continue;
            }

            if (text[i] == '"')
            {
                return i + 1;
            }

            masked[i] = text[i] == '\n' ? '\n' : ' ';
            i++;
        }

        return i;
    }

    private static int MaskLiteral(string text, char[] masked, int from)
    {
        char quote = text[from];
        int i = from + 1;
        while (i < text.Length && text[i] != quote && text[i] != '\n')
        {
            masked[i] = ' ';
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                masked[i + 1] = ' ';
                i++;
            }

            i++;
        }

        return i + 1;
    }

    private static char Next(string text, int i) => i + 1 < text.Length ? text[i + 1] : '\0';

    private static int Blank(char[] masked, int from, int count)
    {
        for (int i = from; i < from + count && i < masked.Length; i++)
        {
            masked[i] = ' ';
        }

        return from + count;
    }

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// Reported with <c>/</c> throughout. <see cref="Path.GetRelativePath"/>
    /// returns the platform separator, so a backslash in an expected string is
    /// green on Windows and red on Linux CI — this repository has been bitten by
    /// exactly that.
    /// </summary>
    private static string Relative(DirectoryInfo root, string file) =>
        Path.GetRelativePath(root.FullName, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string ReadRepositoryFile(string relative) =>
        File.ReadAllText(Path.Combine(RepositoryRoot().FullName, relative))
            .Replace("\r", string.Empty, StringComparison.Ordinal);

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

    private enum AccessKind
    {
        Unclassified,
        Authorized,
        Anonymous,
    }

    private enum Visibility
    {
        /// <summary>The antecedent is a call in the mapping's own chain, or the group it was written on.</summary>
        Chain,

        /// <summary>One hop: the method group the chain names, its body or its signature.</summary>
        HandlerBody,

        /// <summary>Application, a shared handler, or another process. A register is the ceiling.</summary>
        None,
    }

    private sealed record Mechanism(
        string Identifier,
        string Name,
        string Status,
        Visibility Visibility,
        string? Guard,
        string? GuardMethod);

    private sealed record Surface(
        IReadOnlyList<string> EndpointSources,
        IReadOnlyDictionary<string, string> Masked,
        IReadOnlyList<RouteGroup> Groups,
        IReadOnlyList<RouteMapping> Routes);

    private sealed record RouteGroup(string File, int Line, string Variable, string Prefix, string Chain);

    private sealed record RouteMapping(
        string File,
        int Line,
        string Verb,
        string Route,
        string GroupPrefix,
        string Chain,
        AccessKind Kind)
    {
        public string FullRoute => (GroupPrefix + Route).Replace("//", "/", StringComparison.Ordinal);
    }
}
