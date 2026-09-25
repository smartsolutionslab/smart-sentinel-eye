using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// These types deliberately violate the outbox-commit rule that
/// <see cref="OutboxCommitTests"/> enforces, and exist only to be caught by its
/// walk — they must never be "fixed" by routing <see cref="AsyncOffenderRepository"/>
/// or <see cref="SyncOffenderRepository"/> through <see cref="ITransactionalCommit"/>,
/// and no change here should ever make the probe compliant.
///
/// <para>
/// The expected offender list asserted in
/// <c>OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit</c> is
/// exact, not a superset check. Adding, removing or retyping a member below
/// requires updating that list in the same change, or the probe and the
/// assertion will silently drift apart and stop testing what they claim to.
/// </para>
///
/// <para>
/// <see cref="FailureSubscriptionRepository"/> is not self-evidently a control:
/// it exists because <c>add_SaveChangesFailed</c> is declared on
/// <see cref="DbContext"/> itself (spec 193 §3.2), so a substring or prefix
/// widening of the comparison — <c>Contains("SaveChanges")</c> instead of exact
/// name membership — would misreport a repository that only subscribes to the
/// failure event for logging. It commits nothing and must stay unflagged by
/// any comparison this rule ever ships.
/// </para>
///
/// <para>
/// <see cref="ProbeDbContext"/> is never instantiated — every use below is
/// reflection over declared members and IL, not execution — so it needs no
/// <c>OnConfiguring</c>, no connection string and no <c>DbSet</c>. A
/// parameterless <see cref="DbContext"/> subclass that would throw on
/// construction is fine for exactly that reason.
/// </para>
///
/// <para>
/// Spec 252 (#2470) adds <see cref="MethodGroupOffenderRepository"/>,
/// <see cref="BaseMethodGroupOffenderRepository"/> and
/// <see cref="AsyncLambdaOffenderRepository"/> — the delegate-load and
/// depth-2-nesting shapes the scan stepped over before that fix.
/// </para>
/// </summary>
public sealed class ProbeDbContext : DbContext
{
}

/// <summary>
/// The known offender shape: an awaited <c>SaveChangesAsync</c> call, which the
/// unwidened rule already catches. Its <c>SaveAsync</c> compiles to an async
/// state machine, so the offending call is reached through the nested
/// <c>MoveNext</c>, not the declared method — exactly as the real repositories
/// it stands in for.
/// </summary>
public sealed class AsyncOffenderRepository(ProbeDbContext dbContext)
{
    public async Task SaveAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// The new offender shape this fix must catch: a synchronous
/// <c>SaveChanges()</c> call, reached directly from the declared method. Before
/// spec 193's widening, exact-name comparison against
/// <c>SaveChangesAsync</c> alone lets this pass — that is the gap the issue
/// reported and this type keeps it reproducible.
/// </summary>
public sealed class SyncOffenderRepository(ProbeDbContext dbContext)
{
    public void Save() => dbContext.SaveChanges();
}

/// <summary>
/// The negative control: commits only through the sanctioned seam and never
/// touches <see cref="ProbeDbContext"/> directly. Must stay unflagged before
/// and after spec 193 — without it, the companion assertion cannot tell a
/// detector that is right from one that reports everything it is handed.
/// </summary>
public sealed class SeamCommitRepository(ITransactionalCommit commit)
{
    public async Task SaveAsync(CancellationToken cancellationToken) =>
        await commit.CommitAsync(cancellationToken);
}

/// <summary>
/// The substring-conflict control (spec 193 §3.2). Its constructor calls
/// <c>DbContext.add_SaveChangesFailed</c> — a member whose name *contains*
/// "SaveChanges" and whose declaring type *is* <see cref="DbContext"/>, so
/// the existing declaring-type check does not rescue it from a substring or
/// prefix widening the way it incidentally rescues
/// <c>SaveChangesAndFlushMessagesAsync</c>. It subscribes and commits
/// nothing, and must stay unflagged by exact name membership, which is the
/// comparison spec 193 ships.
/// </summary>
public sealed class FailureSubscriptionRepository
{
    // Kept so this is a genuine instance, not a static-only utility type — the
    // subscription is the point, not the field, but a repository with no
    // instance state at all is a different (and less honest) probe.
    private readonly ProbeDbContext dbContext;

    public FailureSubscriptionRepository(ProbeDbContext dbContext)
    {
        this.dbContext = dbContext;
        this.dbContext.SaveChangesFailed += OnSaveChangesFailed;
    }

    private static void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs args)
    {
        // Empty on purpose: the probe never invokes this. It exists only so the
        // subscription above resolves to a real EventHandler<SaveChangesFailedEventArgs>
        // shape, which is what makes add_SaveChangesFailed appear in the IL at all.
    }
}

/// <summary>
/// Spec 252 (#2470) shape A, live-instance spelling: a method group converted
/// to a delegate compiles to <c>ldvirtftn</c> (<c>0xFE 0x07</c>), not
/// <c>ldftn</c> — <see cref="DbContext.SaveChanges()"/> is virtual, so Roslyn
/// loads it through the vtable even though nothing has been called yet. The
/// commit happens later, through <see cref="Save"/>'s returned delegate, but
/// the reference to <see cref="DbContext.SaveChanges()"/> is already in this
/// type's own IL — which is what the scan must catch.
/// </summary>
public sealed class MethodGroupOffenderRepository(ProbeDbContext dbContext)
{
    public Func<int> Save() => dbContext.SaveChanges;
}

/// <summary>
/// Spec 252 (#2470) shape A, <c>base.</c> spelling: converting
/// <c>base.SaveChanges</c> to a delegate compiles to <c>ldftn</c>
/// (<c>0xFE 0x06</c>) — a non-virtual load, reachable only from inside a
/// <see cref="DbContext"/> subclass. <c>ldftn</c> and <c>ldvirtftn</c> are one
/// opcode family (load a method pointer); a scan that caught only the
/// live-instance spelling above would leave this one uncaught, so each gets
/// its own probe.
/// </summary>
public sealed class BaseMethodGroupOffenderRepository : DbContext
{
    public Func<int> Save() => base.SaveChanges;
}

/// <summary>
/// Spec 252 (#2470) shape B: an <c>async</c> lambda that captures a method
/// parameter (as here) or a local is lifted into a display class and then
/// compiled to a state machine <em>nested inside that display class</em> —
/// <c>AsyncLambdaOffenderRepository+&lt;&gt;c__DisplayClass0_0+&lt;&lt;SaveAsync&gt;b__0&gt;d</c>,
/// depth 2. (A non-capturing or <c>static</c> async lambda takes a different
/// path — the compiler's <c>&lt;&gt;c</c> singleton, not a display class — but
/// still lands at depth 2 and is caught by the same transitive walk; this
/// probe exercises the capturing shape specifically.) The one-level
/// <c>BodiesOf</c> walk that catches a synchronous lambda's closure class
/// never reaches a second level, so this call escapes it. <paramref
/// name="dbContext"/> is taken as a method parameter — not a field —
/// precisely so the lambda is forced into a display class: an async lambda
/// that captures only <c>this</c> (e.g. a primary-constructor field) is
/// lifted directly onto the type instead and stays at depth 1, which the
/// scan already catches today and would make this probe arrive green.
/// </summary>
public static class AsyncLambdaOffenderRepository
{
    public static Task SaveAsync(ProbeDbContext dbContext, Func<Func<CancellationToken, Task>, Task> run) =>
        run(async cancellationToken => await dbContext.SaveChangesAsync(cancellationToken));
}
