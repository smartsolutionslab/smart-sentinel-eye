using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every VariableDto, ResolvedOverlayTextDto and
// PlaceholderResolutionDto field on the mapping as it stands, each with a value distinct from
// its same-typed siblings.
public class SystemVariablesDtoMappingCharacterisationTests
{
    private static readonly FabIdentifier Hamburg = FabIdentifier.From("hamburg");

    private static readonly DateTimeOffset Created =
        DateTimeOffset.Parse("2026-10-01T06:15:00Z", CultureInfo.InvariantCulture);

    private static readonly OperatorIdentifier Definer =
        OperatorIdentifier.From(Guid.Parse("f1f1f1f1-0000-7000-8000-0000000000f1"));

    [Fact]
    public async Task A_boolean_variable_maps_every_field_including_its_labels()
    {
        var variable = new VariableBuilder()
            .WithFab("hamburg")
            .Named("lineRunning")
            .OfType(VariableType.Boolean)
            .WithInitialValue(new VariableValue.BooleanValue(true))
            .WithBooleanLabels(BooleanLabels.From("RUNNING", "STOPPED"))
            .DefinedBy(Definer)
            .At(Created)
            .Build();
        GetVariableQueryHandler handler = new(new TestVariableQuerySource([variable]));

        var result = await handler.HandleAsync(
            new GetVariableQuery([Hamburg], VariableName.From("lineRunning")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.VariableIdentifier.ShouldBe(variable.Id.Value);
        dto.Version.ShouldBe(variable.Version);
        dto.Fab.ShouldBe("hamburg");
        dto.Name.ShouldBe("lineRunning");
        dto.Type.ShouldBe("Boolean");
        dto.State.ShouldBe("Defined");
        dto.Value.ShouldBe(new VariableValue.BooleanValue(true).ToWireString());
        dto.TruthyLabel.ShouldBe("RUNNING");
        dto.FalsyLabel.ShouldBe("STOPPED");
        dto.CreatedAt.ShouldBe(Created);
        dto.CreatedBy.ShouldBe(Definer.Value);
    }

    [Fact]
    public async Task A_non_boolean_variable_maps_null_labels()
    {
        var variable = new VariableBuilder()
            .WithFab("hamburg")
            .Named("oeeLine1")
            .OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5))
            .Build();
        GetVariableQueryHandler handler = new(new TestVariableQuerySource([variable]));

        var result = await handler.HandleAsync(
            new GetVariableQuery([Hamburg], VariableName.From("oeeLine1")), CancellationToken.None);

        result.Value.TruthyLabel.ShouldBeNull();
        result.Value.FalsyLabel.ShouldBeNull();
        result.Value.Value.ShouldBe("82.5");
    }

    [Fact]
    public async Task The_snapshot_maps_each_text_with_its_template_and_its_resolved_form()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repository = new();
        repository.Add(new VariableBuilder().WithFab("hamburg").Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build());
        var overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, ["OEE: {{oeeLine1}}%", "Static caption"]);
        GetOverlaySnapshotQueryHandler handler = new(index, repository, new Resolver(), new FakeOverlayTextVersions());

        var result = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([Hamburg], overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Texts.Count.ShouldBe(2);
        var templates = result.Value.Texts.Select(text => text.Template).Order(StringComparer.Ordinal).ToArray();
        templates.ShouldBe(["OEE: {{oeeLine1}}%", "Static caption"]);
        var oee = result.Value.Texts.Single(text => text.Template == "OEE: {{oeeLine1}}%");
        oee.Resolved.ShouldBe("OEE: 82.5%");
        var caption = result.Value.Texts.Single(text => text.Template == "Static caption");
        caption.Resolved.ShouldBe("Static caption");
    }

    [Fact]
    public async Task A_resolved_boolean_renders_its_own_falsy_label()
    {
        InMemoryVariableRepository repository = new();
        repository.Add(new VariableBuilder().WithFab("hamburg").Named("doorOpen").OfType(VariableType.Boolean)
            .WithInitialValue(new VariableValue.BooleanValue(false)).WithBooleanLabels(BooleanLabels.From("OPEN", "CLOSED")).Build());
        ResolveOverlayTextQueryHandler handler = new(new VariableSnapshotBuilder(repository), new Resolver());

        var result = await handler.HandleAsync(
            new ResolveOverlayTextQuery([Hamburg], "{{doorOpen}}"), CancellationToken.None);

        var placeholder = result.Value.Placeholders.ShouldHaveSingleItem();
        placeholder.Name.ShouldBe("doorOpen");
        placeholder.Outcome.ShouldBe("Resolved");
        placeholder.Fab.ShouldBe("hamburg");
        placeholder.RenderedValue.ShouldBe("CLOSED");
        result.Value.ResolvedText.ShouldBe("CLOSED");
    }

    [Fact]
    public async Task Unresolved_placeholders_carry_a_null_rendered_value()
    {
        InMemoryVariableRepository repository = new();
        repository.Add(new VariableBuilder().WithFab("hamburg").Named("shift").OfType(VariableType.String).Build());
        ResolveOverlayTextQueryHandler handler = new(new VariableSnapshotBuilder(repository), new Resolver());

        var result = await handler.HandleAsync(
            new ResolveOverlayTextQuery([Hamburg], "{{shift}} {{ghost}}"), CancellationToken.None);

        var shift = result.Value.Placeholders.Single(placeholder => placeholder.Name == "shift");
        shift.Outcome.ShouldBe("Unset");
        shift.Fab.ShouldBe("hamburg");
        shift.RenderedValue.ShouldBeNull();
        var ghost = result.Value.Placeholders.Single(placeholder => placeholder.Name == "ghost");
        ghost.Outcome.ShouldBe("Unknown");
        ghost.Fab.ShouldBeNull();
        ghost.RenderedValue.ShouldBeNull();
    }
}
