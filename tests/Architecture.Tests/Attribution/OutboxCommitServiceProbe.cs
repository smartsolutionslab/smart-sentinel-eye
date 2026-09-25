using SmartSentinelEye.Architecture.Tests.Persistence;

namespace SmartSentinelEye.Architecture.Tests.Attribution;

/// <summary>
/// Spec 247 / #2469. The shape the pre-widening candidate filter in
/// <see cref="OutboxCommitTests"/> could not see: a direct
/// <c>SaveChangesAsync</c> commit from a type whose namespace does not
/// contain ".Persistence" and whose name does not end in "Repository" —
/// exactly <c>StreamFabAttributionService</c>'s shape (spec 247 §1). This
/// type exists only to be caught once the filter widens to every top-level
/// type; it must never be "fixed" by routing it through
/// <see cref="ITransactionalCommit"/>, and no change here should ever make
/// the probe compliant.
///
/// <para>
/// Deliberately in a namespace that ends <c>.Attribution</c>, not
/// <c>.Persistence</c>, and named <c>Service</c>, not <c>Repository</c> — the
/// narrow filter's two escapes at once. Reuses <see cref="ProbeDbContext"/>
/// rather than declaring a second inert <c>DbContext</c> subclass: nothing
/// below is ever instantiated, so a second reflection-only context would add
/// nothing this one does not already give.
/// </para>
///
/// <para>
/// The expected offender list asserted in
/// <c>OutboxCommitTests.The_rule_sees_both_spellings_of_a_direct_commit</c>
/// gains this type's full name in the same change that adds it here — see the
/// doc on <see cref="ProbeDbContext"/> for why that list is exact, not a
/// superset check.
/// </para>
/// </summary>
public sealed class OffenderAttributionService(ProbeDbContext dbContext)
{
    public async Task AttributeAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
