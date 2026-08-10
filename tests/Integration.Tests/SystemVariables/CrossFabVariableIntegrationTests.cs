using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Infrastructure.Persistence;
using VariableAggregate = SmartSentinelEye.SystemVariables.Domain.Variable.Variable;

namespace SmartSentinelEye.Integration.Tests.SystemVariables;

/// <summary>
/// Spec 014 T016 — fab scoping of the stored value, against the real stack.
/// Covers SC-001 (two fabs keep their own values) and SC-003 (one name, two
/// fabs).
///
/// <para>
/// The handler tests prove the duplicate-name check consults the fab, but they
/// run against an in-memory double the test itself populates. This exercises
/// what they stub: the real migration's <c>fab</c> column, its backfill, and
/// the <c>(fab, name)</c> partial unique index — the last of which is the only
/// thing that can prove the migration *swapped* the index rather than merely
/// adding a column beside it.
/// </para>
///
/// <para>
/// Variables are seeded through a <c>DbContext</c> rather than the HTTP API,
/// because the API attributes every definition to munich until spec 014 T023
/// resolves the caller's fab. Authoring a dresden variable over HTTP is not
/// possible yet, so seeding is setup here rather than the behaviour under
/// test. When T023 lands, <c>VariableFabResolutionIntegrationTests</c> (T030)
/// covers the authoring path and these stay as they are.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabVariableIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await aspire.ResetSystemVariablesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// SC-003. Before spec 014 the unique index was on <c>name</c> alone, so
    /// the second insert here failed outright. That it now succeeds is the
    /// index swap observed rather than assumed.
    /// </summary>
    [Fact]
    public async Task The_same_variable_name_is_accepted_in_two_fabs()
    {
        string name = UniqueName();
        await SeedNumberAsync("munich", name, 41);
        await SeedNumberAsync("dresden", name, 7);

        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableName parsed = VariableName.From(name);

        List<VariableAggregate> stored = await context.Variables
            .Where(variable => variable.Name == parsed)
            .ToListAsync();

        stored.Count.ShouldBe(2);
        stored.Select(variable => variable.Fab.Value).ShouldBe(["munich", "dresden"], ignoreOrder: true);
    }

    /// <summary>
    /// SC-001. The assertion that matters is on <b>dresden</b>: a version that
    /// only checked munich's new value would pass just as well if the write
    /// were still global, because munich is the fab it would have landed in
    /// either way.
    /// </summary>
    [Fact]
    public async Task Setting_one_fabs_value_leaves_the_other_untouched()
    {
        string name = UniqueName();
        await SeedNumberAsync("munich", name, 41);
        await SeedNumberAsync("dresden", name, 7);

        // The API resolves to munich until T023, which is what makes this the
        // munich row and not an arbitrary one of the two.
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        HttpResponseMessage updated = await VariableRequests.SetValueAsync(variables, name, "99");
        updated.EnsureSuccessStatusCode();

        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableName parsed = VariableName.From(name);
        FabIdentifier munichFab = FabIdentifier.From("munich");
        FabIdentifier dresdenFab = FabIdentifier.From("dresden");

        VariableAggregate munich = await context.Variables
            .SingleAsync(variable => variable.Name == parsed && variable.Fab == munichFab);
        VariableAggregate dresden = await context.Variables
            .SingleAsync(variable => variable.Name == parsed && variable.Fab == dresdenFab);

        munich.Value.ToWireString().ShouldBe("99");
        dresden.Value.ToWireString().ShouldBe("7");
    }

    /// <summary>
    /// Archiving releases the name for re-use, and scoping the index to a fab
    /// must not have quietly taken that away — the partial filter is the part
    /// of the index most easily lost in a hand-corrected migration.
    /// </summary>
    [Fact]
    public async Task An_archived_name_is_free_for_reuse_within_the_same_fab()
    {
        string name = UniqueName();
        await SeedNumberAsync("munich", name, 41, archived: true);

        await SeedNumberAsync("munich", name, 5);

        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();
        VariableName parsed = VariableName.From(name);

        (await context.Variables.CountAsync(variable => variable.Name == parsed)).ShouldBe(2);
    }

    private async Task SeedNumberAsync(string fab, string name, double value, bool archived = false)
    {
        await using SystemVariablesDbContext context =
            await aspire.CreateSystemVariablesDbContextAsync();

        SystemClock clock = new();
        OperatorIdentifier author = OperatorIdentifier.From(Guid.CreateVersion7());
        VariableAggregate variable = VariableAggregate.Define(
            FabIdentifier.From(fab),
            VariableName.From(name),
            VariableType.Number,
            new VariableValue.NumberValue(value),
            booleanLabels: null,
            author,
            clock);

        if (archived)
        {
            variable.Archive(author, clock);
        }
        variable.ClearPendingEvents();

        context.Variables.Add(variable);
        await context.SaveChangesAsync();
    }

    private static string UniqueName() => $"v{Guid.NewGuid():N}"[..12];
}
