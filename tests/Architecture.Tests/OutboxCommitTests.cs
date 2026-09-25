using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Architecture.Tests.Persistence;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 021 FR-007. A repository that calls <c>SaveChanges</c> or
/// <c>SaveChangesAsync</c> directly commits its rows and leaves the
/// announcements behind — which is the defect this feature closed, and the
/// one a repository added later reintroduces by default, because that is what
/// every EF tutorial shows.
///
/// <para>
/// The guarantee is a property of a call site, not of a type, so nothing in the
/// type system holds it. This test is what holds it. It is deliberately a rule
/// with no exemption list: the one repository that announces nothing
/// (<c>DeadLetterRepository</c>) commits through the same seam anyway, precisely
/// so that this can be absolute. An exemption list rots — the next repository
/// added by copying an exempt one inherits the exemption without the reason.
/// </para>
/// </summary>
public class OutboxCommitTests
{
    private static readonly string[] PersistenceAssemblies =
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
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void No_repository_commits_without_its_announcements(string assemblyName)
    {
        List<string> offenders = Offenders(Assembly.Load(assemblyName));

        offenders.ShouldBeEmpty(
            $"{string.Join(", ", offenders)} calls SaveChanges or SaveChangesAsync directly. "
            + "Commit through ITransactionalCommit instead, so the rows and the integration "
            + "events they announce land in one transaction (spec 021 FR-001). Committing "
            + "directly is silent: the write succeeds, the caller is told the truth, and the "
            + "announcement is never made.");
    }

    public static TheoryData<string> Assemblies()
    {
        TheoryData<string> data = [];
        foreach (string assembly in PersistenceAssemblies)
        {
            data.Add(assembly);
        }

        return data;
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
    /// namespace/name candidate filter, so a gap here is the theory's own
    /// gap, not a copy that could disagree with it.
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
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// Steps 1-4 of the walk: the namespace/name candidate filter, then the IL
    /// scan. Shared by the real theory above and by
    /// <see cref="The_rule_sees_both_spellings_of_a_direct_commit"/> so a gap in
    /// the candidate filter or the detector is the theory's own gap, not a copy
    /// that could silently disagree with it.
    /// </summary>
    private static List<string> Offenders(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(type => type.Namespace?.Contains(".Persistence", StringComparison.Ordinal) == true)
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Where(CallsSaveChangesDirectly)
            .Select(type => type.FullName ?? type.Name)];

    /// <summary>
    /// Reads the IL rather than the source, because the call is what matters and
    /// a comment saying "we use the outbox" is not a constraint. Any reference
    /// to <see cref="DbContext.SaveChanges()"/> or
    /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> from a
    /// repository body is an offence — including one buried in a helper, which
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
