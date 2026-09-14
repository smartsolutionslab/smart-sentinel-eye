using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

/// <summary>
/// Spec 148 T006 — <c>GET /system-variables/resolve</c>'s handler. Covers
/// US1 acceptance scenarios 1-5 and 11.
///
/// <para>
/// <b>Phase 4a — RED, and deliberately not characterisation.</b>
/// <c>ResolveOverlayTextQueryHandler</c> is new: the endpoint it serves never
/// existed, so no test of it was ever green (plan.md "Phase 4a"). It is a
/// phase-4a scaffold that always throws <see cref="NotImplementedException"/>
/// until T004/T005 implement it — every fact below is expected to fail on
/// that exception, not on a missing type.
/// </para>
/// </summary>
public class ResolveOverlayTextQueryHandlerTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    private static ResolveOverlayTextQueryHandler HandlerOver(InMemoryVariableRepository repo) =>
        new(new VariableSnapshotBuilder(repo), new Resolver());

    [Fact]
    public async Task Resolves_a_defined_variable_into_the_text()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("temperature")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(23.4)).Build());

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich], "Line 1: {{temperature}}"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResolvedText.ShouldBe("Line 1: 23.4");
        PlaceholderResolutionDto entry = result.Value.Placeholders.ShouldHaveSingleItem();
        entry.Name.ShouldBe("temperature");
        entry.Outcome.ShouldBe("Resolved");
        entry.Fab.ShouldBe("munich");
        entry.RenderedValue.ShouldBe("23.4");
    }

    /// <summary>
    /// Spec 148 US1 scenario 2 — pins finding 2. <c>RenderedValue</c> must be
    /// <c>VariableValue.Render</c>'s shortest round-trip, never the wire's
    /// <c>G17</c> string. Plan.md risk 1: "the most likely single defect in
    /// this spec."
    /// </summary>
    [Fact]
    public async Task A_number_renders_shortest_round_trip_not_the_wire_G17_string()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("temperature")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(23.4)).Build());

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich], "{{temperature}}"), CancellationToken.None);

        result.Value.Placeholders.Single().RenderedValue.ShouldBe("23.4");
        result.Value.Placeholders.Single().RenderedValue.ShouldNotBe(
            new VariableValue.NumberValue(23.4).ToWireString());
    }

    /// <summary>Spec 148 US1 scenario 3 — boolean labels, not the raw bool.</summary>
    [Fact]
    public async Task A_boolean_renders_its_truthy_label_not_the_raw_boolean()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("lineRunning")
            .OfType(VariableType.Boolean)
            .WithInitialValue(new VariableValue.BooleanValue(true))
            .WithBooleanLabels(BooleanLabels.From("RUNNING", "STOPPED"))
            .Build());

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich], "{{lineRunning}}"), CancellationToken.None);

        result.Value.Placeholders.Single().RenderedValue.ShouldBe("RUNNING");
    }

    /// <summary>
    /// Spec 148 US1 scenario 4 — the resolved text alone cannot distinguish
    /// Unknown / Unset / Archived; the per-name entries must.
    /// </summary>
    [Fact]
    public async Task The_three_unresolvable_outcomes_keep_the_literal_and_are_distinguished_by_entry()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("shift").OfType(VariableType.String).Build());
        VariableBuilder archivedBuilder = new VariableBuilder().WithFab("munich").Named("oldOne")
            .OfType(VariableType.String).WithInitialValue(new VariableValue.StringValue("x"));
        Variable archived = archivedBuilder.Build();
        archived.Archive(archivedBuilder.Operator, archivedBuilder.Clock);
        repo.Add(archived);

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich], "{{shift}} {{oldOne}} {{unknownName}}"), CancellationToken.None);

        result.Value.ResolvedText.ShouldBe("{{shift}} {{oldOne}} {{unknownName}}");
        result.Value.Placeholders.Single(p => p.Name == "shift").Outcome.ShouldBe("Unset");
        result.Value.Placeholders.Single(p => p.Name == "oldOne").Outcome.ShouldBe("Archived");
        result.Value.Placeholders.Single(p => p.Name == "unknownName").Outcome.ShouldBe("Unknown");
    }

    [Fact]
    public async Task Text_with_no_placeholders_is_returned_byte_for_byte_with_an_empty_list()
    {
        InMemoryVariableRepository repo = new();

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich], "PRODUCTION LINE 1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResolvedText.ShouldBe("PRODUCTION LINE 1");
        result.Value.Placeholders.ShouldBeEmpty();
    }

    /// <summary>Spec 148 US1 scenario 11 — the fab that answered is named.</summary>
    [Fact]
    public async Task The_entry_names_the_fab_that_answered_under_the_ordinal_tiebreak()
    {
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("dresden").Named("oee")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(7)).Build());
        repo.Add(new VariableBuilder().WithFab("munich").Named("oee")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());

        Result<ResolvedTextPreviewDto, ResolveOverlayTextError> result = await HandlerOver(repo).HandleAsync(
            new ResolveOverlayTextQuery([Munich, Dresden], "{{oee}}"), CancellationToken.None);

        result.Value.ResolvedText.ShouldBe("7");
        result.Value.Placeholders.Single().Fab.ShouldBe("dresden");
    }
}
