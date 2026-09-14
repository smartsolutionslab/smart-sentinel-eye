using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Resolution;

/// <summary>
/// Spec 148 T003 — the four <see cref="PlaceholderOutcome"/> cases the
/// extracted <see cref="VariableSnapshotBuilder"/> must produce, plus the two
/// facts that only a multi-name or multi-fab list can show: a label mixing
/// every outcome resolves each independently, and the per-placeholder
/// ordinal fab tiebreak (ADR-0115) is visible on the result rather than only
/// on the resolved text.
///
/// <para>
/// <b>Phase 4a — RED.</b> <see cref="VariableSnapshotBuilder"/> is a phase-4a
/// scaffold that always throws <see cref="NotImplementedException"/>
/// (spec 148 plan.md "The extraction", T002). Every fact below is expected
/// to fail until T002 moves <c>GetOverlaySnapshotQueryHandler.BuildSnapshotAsync</c>
/// and <c>FindInAnyFabAsync</c> here and maps their exits onto
/// <see cref="PlaceholderOutcome"/>.
/// </para>
/// </summary>
public class VariableSnapshotBuilderTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task A_defined_set_non_archived_variable_resolves()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("temperature")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(23.4)).Build());

        VariableSnapshotBuilder builder = new(repo);

        IReadOnlyList<PlaceholderResolution> resolutions =
            await builder.BuildAsync([Munich], "{{temperature}}", CancellationToken.None);

        PlaceholderResolution resolution = resolutions.ShouldHaveSingleItem();
        resolution.Name.ShouldBe("temperature");
        resolution.Outcome.ShouldBe(PlaceholderOutcome.Resolved);
        resolution.Fab.ShouldBe(Munich);
        resolution.Entry.ShouldNotBeNull();
        resolution.Entry.Value.ShouldBe(new VariableValue.NumberValue(23.4));
    }

    [Fact]
    public async Task A_name_defined_nowhere_the_caller_holds_is_Unknown()
    {
        InMemoryVariableRepository repo = new();
        VariableSnapshotBuilder builder = new(repo);

        IReadOnlyList<PlaceholderResolution> resolutions =
            await builder.BuildAsync([Munich], "{{temperatuer}}", CancellationToken.None);

        PlaceholderResolution resolution = resolutions.ShouldHaveSingleItem();
        resolution.Name.ShouldBe("temperatuer");
        resolution.Outcome.ShouldBe(PlaceholderOutcome.Unknown);
        resolution.Fab.ShouldBeNull();
        resolution.Entry.ShouldBeNull();
    }

    [Fact]
    public async Task A_defined_variable_with_no_value_yet_is_Unset()
    {
        InMemoryVariableRepository repo = new();
        // No WithInitialValue: starts Unset.
        repo.Add(new VariableBuilder().WithFab("munich").Named("shift").OfType(VariableType.String).Build());

        VariableSnapshotBuilder builder = new(repo);

        IReadOnlyList<PlaceholderResolution> resolutions =
            await builder.BuildAsync([Munich], "{{shift}}", CancellationToken.None);

        PlaceholderResolution resolution = resolutions.ShouldHaveSingleItem();
        resolution.Outcome.ShouldBe(PlaceholderOutcome.Unset);
        resolution.Entry.ShouldBeNull();
    }

    [Fact]
    public async Task An_archived_variable_is_Archived()
    {
        InMemoryVariableRepository repo = new();
        VariableBuilder builder = new VariableBuilder().WithFab("munich").Named("oldOne")
            .OfType(VariableType.String).WithInitialValue(new VariableValue.StringValue("x"));
        Variable variable = builder.Build();
        variable.Archive(builder.Operator, builder.Clock);
        repo.Add(variable);

        VariableSnapshotBuilder snapshotBuilder = new(repo);

        IReadOnlyList<PlaceholderResolution> resolutions =
            await snapshotBuilder.BuildAsync([Munich], "{{oldOne}}", CancellationToken.None);

        PlaceholderResolution resolution = resolutions.ShouldHaveSingleItem();
        resolution.Outcome.ShouldBe(PlaceholderOutcome.Archived);
        resolution.Entry.ShouldBeNull();
    }

    /// <summary>
    /// One label, all four outcomes, each resolved independently — the fact a
    /// single-placeholder test cannot show.
    /// </summary>
    [Fact]
    public async Task A_label_mixing_all_four_outcomes_resolves_each_independently()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("temperature")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(23.4)).Build());
        repo.Add(new VariableBuilder().WithFab("munich").Named("shift").OfType(VariableType.String).Build());
        VariableBuilder archivedBuilder = new VariableBuilder().WithFab("munich").Named("oldOne")
            .OfType(VariableType.String).WithInitialValue(new VariableValue.StringValue("x"));
        Variable archived = archivedBuilder.Build();
        archived.Archive(archivedBuilder.Operator, archivedBuilder.Clock);
        repo.Add(archived);

        VariableSnapshotBuilder snapshotBuilder = new(repo);

        IReadOnlyList<PlaceholderResolution> resolutions = await snapshotBuilder.BuildAsync(
            [Munich], "{{temperature}} {{shift}} {{oldOne}} {{unknownName}}", CancellationToken.None);

        resolutions.Count.ShouldBe(4);
        resolutions.Single(r => r.Name == "temperature").Outcome.ShouldBe(PlaceholderOutcome.Resolved);
        resolutions.Single(r => r.Name == "shift").Outcome.ShouldBe(PlaceholderOutcome.Unset);
        resolutions.Single(r => r.Name == "oldOne").Outcome.ShouldBe(PlaceholderOutcome.Archived);
        resolutions.Single(r => r.Name == "unknownName").Outcome.ShouldBe(PlaceholderOutcome.Unknown);
    }

    /// <summary>
    /// ADR-0115's per-placeholder ordinal tiebreak, visible on the result
    /// itself rather than only inferable from which value won.
    /// </summary>
    [Fact]
    public async Task A_name_held_in_two_fabs_resolves_in_the_first_by_ordinal_fab_name()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("dresden").Named("oee")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(7)).Build());
        repo.Add(new VariableBuilder().WithFab("munich").Named("oee")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());

        VariableSnapshotBuilder builder = new(repo);

        // Named in reverse ordinal order — "dresden" still wins because it
        // sorts first, not because it was listed first.
        IReadOnlyList<PlaceholderResolution> resolutions =
            await builder.BuildAsync([Munich, Dresden], "{{oee}}", CancellationToken.None);

        PlaceholderResolution resolution = resolutions.ShouldHaveSingleItem();
        resolution.Outcome.ShouldBe(PlaceholderOutcome.Resolved);
        resolution.Fab.ShouldBe(Dresden);
        resolution.Entry.ShouldNotBeNull();
        resolution.Entry.Value.ShouldBe(new VariableValue.NumberValue(7));
    }
}
