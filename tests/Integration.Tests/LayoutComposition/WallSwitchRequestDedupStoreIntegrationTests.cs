using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.LayoutComposition.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.LayoutComposition;

/// <summary>
/// Spec 296 FR-012, T115 — mirrors
/// <c>VariableValueRequestDedupStoreIntegrationTests</c> exactly: the store
/// is raw SQL relying on <c>INSERT ... ON CONFLICT DO NOTHING</c> against a
/// real primary key, which the EF in-memory provider does not implement, so
/// this has to run against real Postgres (ADR-0103).
///
/// <para>
/// Each test mints its own rule/causing-event/wall identifiers, so no reset
/// is needed and the cases cannot interfere with one another or a rerun.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class WallSwitchRequestDedupStoreIntegrationTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_redelivery_with_the_same_rule_and_causing_event_does_not_reserve_twice()
    {
        await using LayoutCompositionDbContext context = await aspire.CreateLayoutCompositionDbContextAsync();
        WallSwitchRequestDedupStore store = new(context);

        RuleIdentifier rule = RuleIdentifier.From(Guid.CreateVersion7());
        CausingEventIdentifier causing = CausingEventIdentifier.From(Guid.CreateVersion7());
        WallIdentifier wall = WallIdentifier.New();

        bool first = await store.TryReserveAsync(rule, causing, wall, CancellationToken.None);
        bool redelivery = await store.TryReserveAsync(rule, causing, wall, CancellationToken.None);

        first.ShouldBeTrue();
        redelivery.ShouldBeFalse();
    }

    /// <summary>
    /// The #2214 lesson, restated for walls: the rule is part of the key, so
    /// two different rules firing on one causing event both apply.
    /// </summary>
    [Fact]
    public async Task Two_different_rules_firing_on_the_same_causing_event_both_reserve()
    {
        await using LayoutCompositionDbContext context = await aspire.CreateLayoutCompositionDbContextAsync();
        WallSwitchRequestDedupStore store = new(context);

        CausingEventIdentifier causing = CausingEventIdentifier.From(Guid.CreateVersion7());
        WallIdentifier wall = WallIdentifier.New();

        bool ruleA = await store.TryReserveAsync(
            RuleIdentifier.From(Guid.CreateVersion7()), causing, wall, CancellationToken.None);
        bool ruleB = await store.TryReserveAsync(
            RuleIdentifier.From(Guid.CreateVersion7()), causing, wall, CancellationToken.None);

        ruleA.ShouldBeTrue();
        ruleB.ShouldBeTrue();
    }

    [Fact]
    public async Task The_same_rule_firing_on_two_different_causing_events_reserves_independently()
    {
        await using LayoutCompositionDbContext context = await aspire.CreateLayoutCompositionDbContextAsync();
        WallSwitchRequestDedupStore store = new(context);

        RuleIdentifier rule = RuleIdentifier.From(Guid.CreateVersion7());
        WallIdentifier wall = WallIdentifier.New();

        bool first = await store.TryReserveAsync(
            rule, CausingEventIdentifier.From(Guid.CreateVersion7()), wall, CancellationToken.None);
        bool second = await store.TryReserveAsync(
            rule, CausingEventIdentifier.From(Guid.CreateVersion7()), wall, CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeTrue();
    }
}
