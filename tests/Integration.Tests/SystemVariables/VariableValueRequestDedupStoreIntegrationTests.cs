using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 014 T022 — the dedup key includes the fab, asserted against real
/// Postgres.
///
/// <para>
/// T022 names <c>SystemVariables.Infrastructure.Tests</c>, but the store is
/// raw SQL relying on <c>INSERT ... ON CONFLICT DO NOTHING</c> against a real
/// primary key. That project has no database and the EF in-memory provider
/// does not implement the conflict semantics being tested, so asserting there
/// would prove only that the fake agrees with itself. ADR-0103 puts
/// database-dependent tests on the Aspire fixture, which is where this lives.
/// </para>
///
/// <para>
/// Each test mints its own causing event identifier, so no reset is needed and
/// the cases cannot interfere with one another or with a rerun.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class VariableValueRequestDedupStoreIntegrationTests(AspireFixture aspire)
{
    /// <summary>
    /// The case that motivates the whole task: two fabs' rules react to one
    /// ingested event, so both requests carry the same causing event
    /// identifier and the same variable name. Keyed on that pair alone the
    /// second reservation fails and dresden's legitimate change is silently
    /// swallowed as a redelivery.
    /// </summary>
    [Fact]
    public async Task Two_fabs_reserve_the_same_name_and_causing_event_independently()
    {
        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableValueRequestDedupStore store = new(context);

        Guid causing = Guid.CreateVersion7();

        bool munich = await store.TryReserveAsync(
            FabIdentifier.From("munich"), "oeeLine1", causing, CancellationToken.None);
        bool dresden = await store.TryReserveAsync(
            FabIdentifier.From("dresden"), "oeeLine1", causing, CancellationToken.None);

        munich.ShouldBeTrue();
        dresden.ShouldBeTrue();
    }

    /// <summary>
    /// The behaviour the fab must not have cost us: within one fab, a genuine
    /// Wolverine outbox redelivery is still a no-op.
    /// </summary>
    [Fact]
    public async Task A_redelivery_within_one_fab_still_does_not_reserve()
    {
        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableValueRequestDedupStore store = new(context);

        Guid causing = Guid.CreateVersion7();

        bool first = await store.TryReserveAsync(
            FabIdentifier.From("munich"), "oeeLine1", causing, CancellationToken.None);
        bool redelivery = await store.TryReserveAsync(
            FabIdentifier.From("munich"), "oeeLine1", causing, CancellationToken.None);

        first.ShouldBeTrue();
        redelivery.ShouldBeFalse();
    }

    /// <summary>
    /// Two variables in one fab caused by one event are distinct requests —
    /// a rule may set several. The name still has to discriminate.
    /// </summary>
    [Fact]
    public async Task Two_variables_in_one_fab_reserve_independently()
    {
        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableValueRequestDedupStore store = new(context);

        Guid causing = Guid.CreateVersion7();

        bool first = await store.TryReserveAsync(
            FabIdentifier.From("munich"), "oeeLine1", causing, CancellationToken.None);
        bool second = await store.TryReserveAsync(
            FabIdentifier.From("munich"), "oeeLine2", causing, CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeTrue();
    }
}
