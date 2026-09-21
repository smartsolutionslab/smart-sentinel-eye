using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 202 T005 (#2426) — the durable per-overlay counter, against real
/// Postgres. <c>OverlayTextVersionStore</c> does not exist yet (phase 4b);
/// this file defines its exact required behaviour and is compile-red until
/// then — a weaker form of evidence than the true behavioural red in
/// <c>VersionSurvivesARestartTests</c>, and reported as such (tasks.md's own
/// framing of T003–T005).
///
/// <para>
/// Mirrors <see cref="VariableValueRequestDedupStoreIntegrationTests"/> in
/// shape: raw SQL over <c>ON CONFLICT</c> has no in-memory-provider
/// equivalent, so ADR-0103 puts this on the Aspire fixture rather than the
/// Infrastructure unit-test project.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class OverlayTextVersionStoreIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// Spec 202 plan.md §3 — the cutover floor. Copied as a literal, not
    /// referenced from production: the whole point of SC-3 is that a fresh
    /// row must start here rather than at 1, and a test that could not
    /// distinguish the two constants drifting together would not catch that.
    /// </summary>
    private const long Floor = 1_000_000_000;

    [Fact]
    public async Task A_first_ever_advance_for_a_fresh_overlay_returns_the_cutover_floor_not_one()
    {
        await using SystemVariablesDbContext context = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore store = new(context);

        Guid overlay = Guid.CreateVersion7();
        IReadOnlyDictionary<Guid, long> advanced = await store.AdvanceAsync([overlay], CancellationToken.None);

        // SC-3: a durable counter starting from an empty table at 1 would
        // reproduce #2426 once, silently, on the very deploy that fixes it —
        // a kiosk holding a mark from the retired in-memory counter would
        // drop that first push as stale.
        advanced[overlay].ShouldBe(Floor);
    }

    [Fact]
    public async Task A_second_advance_for_the_same_overlay_returns_floor_plus_one()
    {
        await using SystemVariablesDbContext context = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore store = new(context);

        Guid overlay = Guid.CreateVersion7();
        await store.AdvanceAsync([overlay], CancellationToken.None);
        IReadOnlyDictionary<Guid, long> second = await store.AdvanceAsync([overlay], CancellationToken.None);

        second[overlay].ShouldBe(Floor + 1);
    }

    /// <summary>
    /// The restart guarantee itself, proven at unit cost (spec 202 tasks.md
    /// T005c). A store built from an entirely new scope must pick up where
    /// the last one left off — the whole reason this store exists is that
    /// the thing it replaces (<c>InMemoryReverseIndex.versionByOverlay</c>)
    /// could not do this.
    /// </summary>
    [Fact]
    public async Task A_store_from_a_brand_new_scope_continues_from_the_persisted_value()
    {
        Guid overlay = Guid.CreateVersion7();

        await using (SystemVariablesDbContext first = await aspire.CreateSystemVariablesDbContextAsync())
        {
            OverlayTextVersionStore firstScopeStore = new(first);
            await firstScopeStore.AdvanceAsync([overlay], CancellationToken.None);
        }

        // A distinct DbContext from a distinct connection — nothing here is
        // shared with the store above except the row Postgres is holding.
        await using SystemVariablesDbContext second = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore secondScopeStore = new(second);
        IReadOnlyDictionary<Guid, long> advanced =
            await secondScopeStore.AdvanceAsync([overlay], CancellationToken.None);

        advanced[overlay].ShouldBe(
            Floor + 1, "a store built from a new scope restarted the counter instead of persisting it");
    }

    /// <summary>
    /// SC-4 — two variables changing concurrently must not hand the same
    /// overlay the same version twice, or the kiosk's strict drop filter
    /// discards the second as not-newer. <c>ON CONFLICT DO UPDATE</c> takes a
    /// row lock, so the two statements serialise rather than race.
    /// </summary>
    [Fact]
    public async Task Two_concurrent_advances_on_one_overlay_return_two_distinct_increasing_values()
    {
        Guid overlay = Guid.CreateVersion7();

        await using SystemVariablesDbContext contextA = await aspire.CreateSystemVariablesDbContextAsync();
        await using SystemVariablesDbContext contextB = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore storeA = new(contextA);
        OverlayTextVersionStore storeB = new(contextB);

        IReadOnlyDictionary<Guid, long>[] results = await Task.WhenAll(
            storeA.AdvanceAsync([overlay], CancellationToken.None),
            storeB.AdvanceAsync([overlay], CancellationToken.None));

        long first = results[0][overlay];
        long second = results[1][overlay];

        first.ShouldNotBe(second, "two concurrent advances on one overlay produced the same version");
        new[] { first, second }.ShouldBe([Floor, Floor + 1], ignoreOrder: true);
    }

    /// <summary>
    /// R3 — the fan-out handler builds its overlay set from
    /// <c>IReverseIndex.LookupOverlays</c>, which is not guaranteed
    /// duplicate-free across the whole label. The store must de-duplicate
    /// its own input before the <c>INSERT ... ON CONFLICT</c>, or Postgres
    /// raises "ON CONFLICT DO UPDATE command cannot affect row a second
    /// time" and the entire push for every other affected overlay is lost
    /// with it.
    /// </summary>
    [Fact]
    public async Task A_batch_containing_the_same_overlay_twice_does_not_raise_a_conflict_error()
    {
        await using SystemVariablesDbContext context = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore store = new(context);

        Guid repeated = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();

        IReadOnlyDictionary<Guid, long> advanced =
            await store.AdvanceAsync([repeated, other, repeated], CancellationToken.None);

        advanced[repeated].ShouldBe(Floor);
        advanced[other].ShouldBe(Floor);
    }

    /// <summary>
    /// Distinct from "never advanced" vs "advanced to the floor": a pure read
    /// of an overlay this store has never seen must not create a row and must
    /// not answer the floor — it answers <c>0</c>, exactly like the retired
    /// <c>IReverseIndex.CurrentVersionFor</c> did for an unknown overlay.
    /// </summary>
    [Fact]
    public async Task Reading_the_current_version_of_an_untouched_overlay_returns_zero()
    {
        await using SystemVariablesDbContext context = await aspire.CreateSystemVariablesDbContextAsync();
        OverlayTextVersionStore store = new(context);

        long current = await store.CurrentAsync(Guid.CreateVersion7(), CancellationToken.None);

        current.ShouldBe(0);
    }
}
