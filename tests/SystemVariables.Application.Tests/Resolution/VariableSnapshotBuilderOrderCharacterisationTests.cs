using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Resolution;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>VariableSnapshotBuilder.BuildAsync</c> hands its
/// found-variable tail to a helper. One label mixes every outcome across two
/// fabs given in the opposite order to their names, and the whole ordered list
/// is stated as literals: name, outcome, answering fab, entry value and labels.
///
/// <para>
/// The existing <c>VariableSnapshotBuilderTests</c> pin each outcome alone; this
/// pins them together, in label order, with the answering fab on both the
/// Resolved and the Unset row. Counterfactual seen to fail: returning a null fab
/// instead of <c>answeringFab</c> on the Unset row turns
/// <see cref="A_label_mixing_every_outcome_yields_these_rows_in_label_order"/> red.
/// </para>
/// </summary>
public class VariableSnapshotBuilderOrderCharacterisationTests
{
    private static readonly DateTimeOffset DefinedAt =
        DateTimeOffset.Parse("2026-06-01T07:00:00Z", CultureInfo.InvariantCulture);

    private const string Label =
        "{{both}} {{onlyB}} {{unsetOne}} {{archivedOne}} {{unknownOne}} {{both}} {{flag}}";

    [Fact]
    public async Task A_label_mixing_every_outcome_yields_these_rows_in_label_order()
    {
        VariableSnapshotBuilder builder = new(SeededRepository());

        var rows = await builder.BuildAsync(
            [FabIdentifier.From("b-fab"), FabIdentifier.From("a-fab")],
            Label,
            CancellationToken.None);

        Render(rows).ShouldBe(
            [
                "both|Resolved|a-fab|NumberValue:20|-",
                "onlyB|Resolved|b-fab|StringValue:bee|-",
                "unsetOne|Unset|a-fab|-|-",
                "archivedOne|Archived|-|-|-",
                "unknownOne|Unknown|-|-|-",
                "flag|Resolved|a-fab|BooleanValue:true|ON/OFF",
            ]);
    }

    [Fact]
    public async Task A_single_fab_answers_with_that_fab_even_where_the_name_is_defined_in_two()
    {
        VariableSnapshotBuilder builder = new(SeededRepository());

        var rows = await builder.BuildAsync(
            [FabIdentifier.From("b-fab")],
            "{{both}} {{unsetOne}} {{onlyB}}",
            CancellationToken.None);

        Render(rows).ShouldBe(
            [
                "both|Resolved|b-fab|NumberValue:10|-",
                "unsetOne|Unknown|-|-|-",
                "onlyB|Resolved|b-fab|StringValue:bee|-",
            ]);
    }

    [Fact]
    public async Task A_label_with_no_placeholder_yields_no_rows()
    {
        VariableSnapshotBuilder builder = new(SeededRepository());

        var rows = await builder.BuildAsync(
            [FabIdentifier.From("a-fab")],
            "no placeholders {here} {{ spaced }} {{9digit}}",
            CancellationToken.None);

        rows.ShouldBeEmpty();
    }

    private static string[] Render(IReadOnlyList<PlaceholderResolution> rows) =>
        [.. rows.Select(row =>
            $"{row.Name}|{row.Outcome}|{row.Fab?.Value ?? "-"}|{Entry(row.Entry)}|{Labels(row.Entry)}")];

    private static string Entry(VariableSnapshotEntry? entry) =>
        entry is null ? "-" : $"{entry.Value.GetType().Name}:{entry.Value.ToWireString()}";

    private static string Labels(VariableSnapshotEntry? entry) =>
        entry?.BooleanLabels is { } labels ? $"{labels.TruthyLabel}/{labels.FalsyLabel}" : "-";

    private static InMemoryVariableRepository SeededRepository()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(NumberVariable("b-fab", "both", 10));
        repo.Add(NumberVariable("a-fab", "both", 20));
        repo.Add(new VariableBuilder()
            .WithFab("b-fab")
            .Named("onlyB")
            .OfType(VariableType.String)
            .WithInitialValue(new VariableValue.StringValue("bee"))
            .At(DefinedAt)
            .Build());
        repo.Add(new VariableBuilder()
            .WithFab("a-fab")
            .Named("unsetOne")
            .OfType(VariableType.String)
            .At(DefinedAt)
            .Build());

        var archived = new VariableBuilder()
            .WithFab("b-fab")
            .Named("archivedOne")
            .OfType(VariableType.String)
            .WithInitialValue(new VariableValue.StringValue("gone"))
            .At(DefinedAt)
            .Build();
        archived.Archive(OperatorIdentifier.From(Guid.Parse("e9f8a7b6-c5d4-4e3f-82a1-b0c9d8e7f6a5")), new FakeClock(DefinedAt));
        repo.Add(archived);

        repo.Add(new VariableBuilder()
            .WithFab("a-fab")
            .Named("flag")
            .OfType(VariableType.Boolean)
            .WithInitialValue(new VariableValue.BooleanValue(true))
            .WithBooleanLabels(BooleanLabels.From("ON", "OFF"))
            .At(DefinedAt)
            .Build());

        return repo;
    }

    private static Variable NumberVariable(string fab, string name, double value) =>
        new VariableBuilder()
            .WithFab(fab)
            .Named(name)
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(value))
            .At(DefinedAt)
            .Build();
}
