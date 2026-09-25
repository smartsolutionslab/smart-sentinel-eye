using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Architecture.Tests.Persistence;
using SmartSentinelEye.StreamDistribution.Infrastructure.Attribution;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 021 FR-007. A type that calls <c>SaveChanges</c> or
/// <c>SaveChangesAsync</c> directly commits its rows and leaves the
/// announcements behind — which is the defect this feature closed, and the
/// one a repository added later reintroduces by default, because that is what
/// every EF tutorial shows.
///
/// <para>
/// The guarantee is a property of a call site, not of a type, so nothing in the
/// type system holds it. This test is what holds it. It has one recorded
/// exception (<see cref="PermittedDirectCommits"/>), not a design where every
/// repository must earn its way onto a list: <c>DeadLetterRepository</c>, the
/// one repository that announces nothing, still commits through the same seam
/// anyway, so the rule stays absolute for every repository that could
/// plausibly be copied. The rot an exemption list invites — the next
/// repository added by copying an exempt one inherits the exemption without
/// the reason — cannot happen to a list keyed by <c>typeof</c>: a copy is a
/// different <see cref="Type"/> and is reported like anything else (spec 247 /
/// #2469).
/// </para>
///
/// <para>
/// Spec 255 / #2586. The scanned set below is every assembly that can reach a
/// <see cref="DbContext"/>, not only <c>.Infrastructure</c> — held by
/// <see cref="Every_assembly_that_can_reach_a_DbContext_is_scanned"/>.
/// </para>
/// </summary>
public class OutboxCommitTests
{
    private static readonly string[] ScannedAssemblies =
    [
        "SmartSentinelEye.CameraCatalog.Infrastructure",
        "SmartSentinelEye.StreamDistribution.Infrastructure",
        "SmartSentinelEye.LayoutComposition.Infrastructure",
        "SmartSentinelEye.SystemVariables.Infrastructure",
        "SmartSentinelEye.EventIngestion.Infrastructure",
        "SmartSentinelEye.OverlayDesigner.Infrastructure",
        "SmartSentinelEye.Automation.Infrastructure",
        "SmartSentinelEye.Identity.Infrastructure",
        "SmartSentinelEye.AuditObservability.Infrastructure",
        "SmartSentinelEye.CameraCatalog.Api",
        "SmartSentinelEye.StreamDistribution.Api",
        "SmartSentinelEye.LayoutComposition.Api",
        "SmartSentinelEye.SystemVariables.Api",
        "SmartSentinelEye.EventIngestion.Api",
        "SmartSentinelEye.OverlayDesigner.Api",
        "SmartSentinelEye.Automation.Api",
        "SmartSentinelEye.Identity.Api",
        "SmartSentinelEye.AuditObservability.Api",
        "SmartSentinelEye.CameraCatalog.Application",
        "SmartSentinelEye.StreamDistribution.Application",
        "SmartSentinelEye.LayoutComposition.Application",
        "SmartSentinelEye.SystemVariables.Application",
        "SmartSentinelEye.EventIngestion.Application",
        "SmartSentinelEye.OverlayDesigner.Application",
        "SmartSentinelEye.Automation.Application",
        "SmartSentinelEye.Identity.Application",
        "SmartSentinelEye.AuditObservability.Application",
        "SmartSentinelEye.ServiceDefaults",
        "SmartSentinelEye.MigrationRunner",
    ];

    /// <summary>
    /// Spec 247 / #2469. The guard's one recorded exception, keyed by
    /// <c>typeof</c> rather than by namespace or name — so a class that copies
    /// <see cref="StreamFabAttributionService"/>'s shape is still reported;
    /// only this exact type is exempt.
    ///
    /// <para>
    /// Three reasons decide it, in the order that matters (spec 247 §1.2):
    /// (1) complying would not protect anything — the service holds no
    /// <c>IDomainEventDispatcher</c> and does not go through
    /// <c>IStreamRepository</c>, so an event raised by
    /// <c>Stream.AttributeToFab</c> would be lost whichever commit method the
    /// service used, which means forcing it through
    /// <c>ITransactionalCommit</c> would turn this guard green while leaving
    /// the real hazard exactly where it is; (2) the seam commits a different,
    /// scoped <c>DbContext</c> — this service creates its own from
    /// <c>IDbContextFactory</c>, so routing it through the seam means
    /// rewriting how it loads and tracks streams, not swapping one call; (3)
    /// it runs as a hosted service registered before Wolverine builds the
    /// outbox storage, an untested ordering risk the rewrite would carry for
    /// no benefit given (1).
    /// </para>
    ///
    /// <para>
    /// The premise this rests on — that <c>Stream.AttributeToFab</c> raises no
    /// domain event — is pinned by
    /// <c>StreamFabAttributionTests.The_pass_raises_nothing_a_direct_commit_would_drop</c>.
    /// If that test ever fails, the fix is to route the pass through
    /// <c>IStreamRepository.SaveAsync</c> and delete this entry — not to add
    /// another permitted type, and not to adjust that test.
    /// </para>
    ///
    /// <para>
    /// That premise, and the three reasons above it, are argued only against
    /// <c>Attribute(...)</c>'s current call to <c>Stream.AttributeToFab</c> — but
    /// <c>typeof</c>-keying exempts the whole type, not that one call site. Any
    /// domain-mutating call added anywhere else in
    /// <c>StreamFabAttributionService</c> (inside <c>AttributeOnceAsync</c> or a
    /// later method) is silently exempt too, and must be checked against this
    /// same safety argument before this entry can be trusted to still hold.
    /// </para>
    ///
    /// <para>
    /// <see cref="Every_permitted_direct_commit_still_commits_directly"/> is
    /// the other half: it keeps a stale entry from surviving once the
    /// exemption is no longer needed.
    /// </para>
    /// </summary>
    private static readonly Type[] PermittedDirectCommits =
    [
        typeof(StreamFabAttributionService),
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Nothing_commits_without_its_announcements(string assemblyName)
    {
        List<string> offenders = Offenders(Assembly.Load(assemblyName));

        offenders.ShouldBeEmpty(
            $"{string.Join(", ", offenders)} calls SaveChanges or SaveChangesAsync directly. "
            + "Commit through ITransactionalCommit instead, so the rows and the integration "
            + "events they announce land in one transaction (spec 021 FR-001). Committing "
            + "directly is silent: the write succeeds, the caller is told the truth, and the "
            + "announcement is never made.");
    }

    /// <summary>
    /// Spec 247 / #2469. Keeps <see cref="PermittedDirectCommits"/> honest
    /// against the corpus it exempts from. An entry whose assembly is no
    /// longer scanned, or that stopped calling SaveChanges/SaveChangesAsync
    /// directly — because it was routed through <c>ITransactionalCommit</c> —
    /// is a stale exemption: it exempts nothing, and leaving it in would let
    /// the next reader believe this list is still curated when nobody is
    /// checking it any more.
    /// </summary>
    [Fact]
    public void Every_permitted_direct_commit_still_commits_directly()
    {
        foreach (Type permitted in PermittedDirectCommits)
        {
            ScannedAssemblies.ShouldContain(
                permitted.Assembly.GetName().Name,
                $"{permitted.FullName} is in PermittedDirectCommits but its assembly is not one of "
                + "ScannedAssemblies; remove the entry.");

            CallsSaveChangesDirectly(permitted).ShouldBeTrue(
                $"{permitted.FullName} is in PermittedDirectCommits but no longer calls SaveChanges "
                + "or SaveChangesAsync directly; remove the entry — its exemption is no longer needed.");
        }
    }

    public static TheoryData<string> Assemblies()
    {
        TheoryData<string> data = [];
        foreach (string assembly in ScannedAssemblies)
        {
            data.Add(assembly);
        }

        return data;
    }

    /// <summary>
    /// Spec 255 / #2586. Derives, independently of
    /// <see cref="PersistenceAssemblies"/>, which built assemblies can reach a
    /// <see cref="DbContext"/> at all — a direct commit needs either a MemberRef
    /// into EF Core, or a reference to the assembly that defines the concrete
    /// context it would commit, and both are readable from an assembly's own
    /// reference metadata without loading or running any of its code.
    ///
    /// <para>
    /// Failing this fact is not itself an offence: it means
    /// <see cref="PersistenceAssemblies"/> has fallen behind what the build now
    /// produces, the same way the namespace/name candidate filter fell behind
    /// <c>StreamFabAttributionService</c> (spec 247). The fix is to widen that
    /// list — or, if an assembly genuinely cannot commit despite the metadata,
    /// to record why here — never to narrow the criterion below to match
    /// today's list.
    /// </para>
    ///
    /// <para>
    /// <b>Known limitation.</b> Candidates come from files in
    /// <see cref="AppContext.BaseDirectory"/>, which holds only the assemblies
    /// this test project itself references. A new bounded context's DLLs land
    /// there only once it is added to <c>Architecture.Tests.csproj</c>, exactly
    /// as for every other boundary test in this project.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_assembly_that_can_reach_a_DbContext_is_scanned()
    {
        // This test assembly itself references EF Core for the probe DbContext
        // in OutboxCommitProbe.cs (see The_rule_sees_both_spellings_of_a_direct_commit's
        // doc) and is deliberately never a scan candidate — it is not production
        // code that could commit anything.
        string testAssemblyFile = Path.GetFileName(typeof(OutboxCommitTests).Assembly.Location);

        List<string> reaching = [];
        foreach (string path in Directory.GetFiles(AppContext.BaseDirectory, "SmartSentinelEye.*.dll"))
        {
            if (string.Equals(Path.GetFileName(path), testAssemblyFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AssemblyName name = AssemblyName.GetAssemblyName(path);
            Assembly assembly = Assembly.Load(name);

            bool canReachDbContext = assembly.GetReferencedAssemblies().Any(reference =>
                reference.Name == "Microsoft.EntityFrameworkCore"
                || (reference.Name is not null
                    && reference.Name.StartsWith("SmartSentinelEye.", StringComparison.Ordinal)
                    && reference.Name.EndsWith(".Infrastructure", StringComparison.Ordinal)));

            if (canReachDbContext)
            {
                reaching.Add(assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(path));
            }
        }

        // A broken directory probe or filter that silently finds nothing would
        // otherwise pass this fact vacuously; this is an independent fact about
        // the build output, not a restatement of PersistenceAssemblies.
        reaching.ShouldContain(
            "SmartSentinelEye.CameraCatalog.Infrastructure",
            "the probe found no reaching assemblies at all — AppContext.BaseDirectory or the "
            + "SmartSentinelEye.*.dll filter is broken, not that nothing can reach a DbContext.");

        List<string> unscanned =
            [.. reaching.Except(ScannedAssemblies).OrderBy(name => name, StringComparer.Ordinal)];

        unscanned.ShouldBeEmpty(
            $"{string.Join(", ", unscanned)} can reach a DbContext (references "
            + "Microsoft.EntityFrameworkCore, or a SmartSentinelEye.*.Infrastructure assembly) but "
            + "is not in PersistenceAssemblies. Add it to the scanned list — or, if it genuinely "
            + "cannot commit despite this metadata, explain why in this test rather than narrowing "
            + "the criterion above.");
    }

    /// <summary>
    /// Spec 193. The issue's own counterfactual, made permanent: before the
    /// comparison in <see cref="ReferencesSaveChanges"/> widens to both
    /// spellings, <see cref="SyncOffenderRepository"/> commits synchronously
    /// and is not reported — that gap is what this asserts.
    /// <see cref="AsyncOffenderRepository"/> is the spelling the rule already
    /// catches and must stay caught. <see cref="SeamCommitRepository"/> and
    /// <see cref="FailureSubscriptionRepository"/> are the negative and
    /// substring-conflict controls (<c>OutboxCommitProbe.cs</c>) — without
    /// them this assertion could not tell a detector that is right from one
    /// that reports every candidate it is handed.
    ///
    /// <para>
    /// This calls the same <see cref="CallsSaveChangesDirectly"/> the real
    /// theory above calls — not a reimplementation of it — over the same
    /// candidate filter, so a gap here is the theory's own gap, not a copy
    /// that could disagree with it.
    /// </para>
    ///
    /// <para>
    /// The probe types live in this same test assembly, which is never a
    /// member of <see cref="Assemblies"/> — the real theory above never sees
    /// them, so this fact cannot turn it red.
    /// </para>
    ///
    /// <para>
    /// Spec 252 (#2470) adds three more rows for the shapes the scan stepped
    /// over: <c>MethodGroupOffenderRepository</c> proves the live-instance
    /// method-group load (<c>ldvirtftn</c>), <c>BaseMethodGroupOffenderRepository</c>
    /// proves the <c>base.</c> spelling (<c>ldftn</c>), and
    /// <c>AsyncLambdaOffenderRepository</c> proves an async lambda's state
    /// machine nested two levels deep (display class, then state machine) is
    /// still reached. Each is red on arrival — the detector this fact drives
    /// does not yet see any of the three — and the fix that follows must turn
    /// all three green without editing this list again.
    /// </para>
    ///
    /// <para>
    /// Spec 247. <see cref="Attribution.OffenderAttributionService"/> is the
    /// gap the namespace/name filter left open: a direct commit outside
    /// ".Persistence" and not named "...Repository". Its full name below is
    /// what widening the filter must newly surface, and only it — before the
    /// fix commit this row is missing from the actual list, which is the
    /// probe's own red.
    /// </para>
    /// </summary>
    [Fact]
    public void The_rule_sees_both_spellings_of_a_direct_commit()
    {
        List<string> offenders = Offenders(typeof(OutboxCommitTests).Assembly);

        offenders.ShouldBe(
            [
                "SmartSentinelEye.Architecture.Tests.Persistence.AsyncOffenderRepository",
                "SmartSentinelEye.Architecture.Tests.Persistence.SyncOffenderRepository",
                "SmartSentinelEye.Architecture.Tests.Persistence.MethodGroupOffenderRepository",
                "SmartSentinelEye.Architecture.Tests.Persistence.BaseMethodGroupOffenderRepository",
                "SmartSentinelEye.Architecture.Tests.Persistence.AsyncLambdaOffenderRepository",
                "SmartSentinelEye.Architecture.Tests.Attribution.OffenderAttributionService",
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// Steps 1-4 of the walk: the candidate filter, then the IL scan. Shared by
    /// the real theory above and by
    /// <see cref="The_rule_sees_both_spellings_of_a_direct_commit"/> so a gap in
    /// the candidate filter or the detector is the theory's own gap, not a copy
    /// that could silently disagree with it.
    ///
    /// <para>
    /// Spec 247 / #2469. Candidates are every top-level type in the assembly
    /// (<c>!type.IsNested</c>), not only ones in a ".Persistence" namespace
    /// named "...Repository". That narrower filter is a proxy for "code that
    /// commits" which assumes commits happen in repositories; it missed
    /// <c>StreamFabAttributionService</c>, a hosted service that takes an
    /// <c>IDbContextFactory</c> and commits directly, precisely the shape the
    /// next background sweep or backfill is likely to repeat. Nested types are
    /// excluded, not admitted: <see cref="BodiesOf"/> already walks a type's
    /// direct nested types (including its async state machines) through its
    /// declaring type, so admitting them here would report the same offence
    /// twice, once under a compiler-generated name. Nested-of-nested bodies are
    /// not walked — a pre-existing gap, tracked separately (#2470).
    /// </para>
    /// </summary>
    private static List<string> Offenders(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(type => !type.IsNested)
            .Where(type => !PermittedDirectCommits.Contains(type))
            .Where(CallsSaveChangesDirectly)
            .Select(type => type.FullName ?? type.Name)];

    /// <summary>
    /// Reads the IL rather than the source, because the call is what matters and
    /// a comment saying "we use the outbox" is not a constraint. Any reference
    /// to <see cref="DbContext.SaveChanges()"/> or
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> from any
    /// type's body is an offence — including one buried in a helper, which
    /// is how it would come back.
    ///
    /// <para>
    /// <b>The nested types are the point.</b> Every repository's <c>SaveAsync</c>
    /// is <c>async</c>, so its body is compiled into a state machine
    /// (<c>CameraRepository+&lt;SaveAsync&gt;d__7.MoveNext</c>) and the declared
    /// method holds nothing but <c>AsyncTaskMethodBuilder</c> plumbing. Scanning
    /// only declared methods — which is what the first version of this rule did —
    /// therefore catches the one shape nobody writes and misses every
    /// <c>await dbContext.SaveChangesAsync(ct)</c> in the repository.
    /// </para>
    ///
    /// <para>
    /// <b>Depth 2, not just depth 1.</b> An <c>async</c> lambda that captures a
    /// parameter or a local is lifted into a closure display-class, and the
    /// lambda's own body is then compiled into a state machine nested
    /// <em>inside that display class</em> —
    /// <c>Type+&lt;&gt;c__DisplayClass0_0+&lt;&lt;Method&gt;b__0&gt;d</c> —
    /// because the state machine still needs the captured values the display
    /// class holds. A non-capturing or <c>static</c> async lambda has nothing to
    /// hold, so it takes a different path: the compiler's <c>&lt;&gt;c</c>
    /// singleton, not a display class, with the state machine nested inside
    /// <em>that</em> instead — <c>Type+&lt;&gt;c+&lt;&lt;Method&gt;b__0&gt;d</c>.
    /// Both shapes land at depth 2, and the non-capturing one is the more common
    /// shape in this codebase's own EF idiom — e.g.
    /// <c>strategy.ExecuteAsync(state, async (ctx, ct) =&gt; await
    /// ctx.SaveChangesAsync(ct), ...)</c> captures nothing, so it goes through
    /// <c>&lt;&gt;c</c>. A walk that stops at the candidate's immediate nested
    /// types finds the display class or the <c>&lt;&gt;c</c> singleton but never
    /// looks inside it, so the walk here is transitive: every nested type, and
    /// every type nested inside that, all the way down. An async lambda that
    /// captures only <c>this</c> (e.g. via a primary-constructor field) needs no
    /// display class either — it is lifted directly onto the type — so it stays
    /// at depth 1 and was already reached before this walk became transitive.
    /// </para>
    /// </summary>
    private static bool CallsSaveChangesDirectly(Type type) =>
        BodiesOf(type).Any(body => ReferencesSaveChanges(body, type.Module));

    private static IEnumerable<MethodBody> BodiesOf(Type type)
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        IEnumerable<Type> types = [type, .. NestedTypesOf(type)];

        return types
            .SelectMany(candidate => candidate.GetMethods(Declared).Cast<MethodBase>()
                .Concat(candidate.GetConstructors(Declared)))
            .Select(method => method.GetMethodBody())
            .Where(body => body is not null)
            .Select(body => body!);
    }

    private static IEnumerable<Type> NestedTypesOf(Type type) =>
        type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(nested => (IEnumerable<Type>)[nested, .. NestedTypesOf(nested)]);

    /// <summary>
    /// Scans a single method body's IL for a reference to
    /// <see cref="DbContext.SaveChanges()"/> or
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>, whether
    /// invoked directly or loaded as a method-group delegate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this does not see, on purpose</b> (spec 252 / #2470):
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Interface dispatch.</b> A <see cref="DbContext"/> reached through an
    /// interface resolves to the interface's method, whose declaring type is not
    /// a <see cref="DbContext"/> — the declaring-type check that rescues
    /// <c>SaveChangesAndFlushMessagesAsync</c> would exclude it by the same
    /// reasoning. No such interface exists in <c>src/</c> today; introducing one
    /// is a design change, and this guard needs revisiting alongside it.
    /// </description></item>
    /// <item><description>
    /// <b><see cref="DbContext.Database"/>.ExecuteSql*, and separately the
    /// <c>IQueryable</c> extension methods <c>ExecuteUpdate*</c> /
    /// <c>ExecuteDelete*</c> (not <c>Database</c> members at all).</b>
    /// These join the ambient transaction rather than bypass it, so whether one
    /// escapes the outbox is a runtime fact (was there an ambient transaction?),
    /// not a call-site fact an IL scan can decide. Eight live
    /// <c>ExecuteSql*</c> sites on 2026-09-25 (no <c>ExecuteUpdate*</c>/
    /// <c>ExecuteDelete*</c> occurrences), four in scanned assemblies, none
    /// announcing anything they commit. A rule for a raw-SQL write against an
    /// announcing aggregate is a different guard, with a different instrument.
    /// </description></item>
    /// <item><description>
    /// <b>Reflection, <c>dynamic</c>, expression trees.</b> None of these leaves
    /// a matching IL call site at the point where the code is written — the
    /// actual call is assembled or dispatched at runtime — so there is nothing
    /// here for a pattern match to see.
    /// </description></item>
    /// </list>
    /// </remarks>
    private static bool ReferencesSaveChanges(MethodBody body, Module module)
    {
        byte[] il = body.GetILAsByteArray() ?? [];

        // 0x28 call, 0x6F callvirt — a direct or virtual invocation.
        // 0xFE 0x06 ldftn, 0xFE 0x07 ldvirtftn — a method group converted to a
        // delegate: the commit is deferred to whoever invokes the delegate, not
        // avoided, so it is the same offence one step removed. ldvirtftn is the
        // live-instance spelling: SaveChanges is virtual, so Roslyn loads it
        // through the vtable even for a method group taken off a live instance.
        // ldftn only appears for the base-qualified spelling, reachable from
        // inside a DbContext subclass.
        for (int i = 0; i + 4 < il.Length; i++)
        {
            int operand = il[i] switch
            {
                0x28 or 0x6F => i + 1,
                0xFE when il[i + 1] is 0x06 or 0x07 => i + 2,
                _ => -1,
            };

            if (operand < 0 || operand + 4 > il.Length)
            {
                continue;
            }

            int token = BitConverter.ToInt32(il, operand);
            try
            {
                MethodBase? called = module.ResolveMethod(token);
                // IsAssignableFrom, not IsSubclassOf: SaveChanges/SaveChangesAsync are
                // declared on DbContext itself, so a subclass check excludes the only
                // declaring type they ever have — which is how the first version of
                // this rule passed against a repository deliberately broken to fail it.
                //
                // Exact name membership, not StartsWith/Contains("SaveChanges"): that
                // substring also matches DbContext.add_SaveChangesFailed and
                // remove_SaveChangesFailed, which are declared on DbContext itself, so
                // the declaring-type check above would not rescue a repository that
                // only subscribes to the failure event for logging. It would also keep
                // matching Wolverine's SaveChangesAndFlushMessagesAsync — the sanctioned
                // seam — by accident of substring rather than by the declaring-type
                // check that actually rescues it.
                if (called?.Name is nameof(DbContext.SaveChanges) or nameof(DbContext.SaveChangesAsync)
                    && typeof(DbContext).IsAssignableFrom(called.DeclaringType))
                {
                    return true;
                }
            }
            catch (Exception resolution) when (
                resolution is ArgumentException
                or FileNotFoundException
                or BadImageFormatException
                or MissingMethodException)
            {
                // Not a method token at this offset — the byte was operand data
                // rather than an opcode. Scanning IL without decoding it fully
                // means this happens, and a bogus token can fail to resolve in
                // several ways, not only as an ArgumentException. None of them
                // is a finding.
            }
        }

        return false;
    }
}
