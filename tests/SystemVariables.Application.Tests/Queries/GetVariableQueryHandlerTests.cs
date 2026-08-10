using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class GetVariableQueryHandlerTests
{
    [Fact]
    public async Task Returns_VariableNotFound_when_no_variable_with_that_name_exists()
    {
        TestVariableQuerySource source = new([]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetVariableError.VariableNotFound>();
    }

    // Without the version on the read side a caller has nothing to put in
    // If-Match, and the cross-request check degrades to no check (ADR-0113).
    [Fact]
    public async Task The_dto_carries_the_aggregate_version()
    {
        Variable variable = new VariableBuilder()
            .Named("oeeLine2").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(1)).Build();

        GetVariableQueryHandler handler = new(new TestVariableQuerySource([variable]));
        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], variable.Name), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(variable.Version);
    }

    [Fact]
    public async Task Returns_a_mapped_DTO_when_the_variable_exists()
    {
        Variable variable = new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build();

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("oeeLine1")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("oeeLine1");
        result.Value.Type.ShouldBe("Number");
        result.Value.State.ShouldBe("Defined");
        result.Value.Value.ShouldBe("82.5");
    }

    [Fact]
    public async Task Maps_Unset_value_to_null_on_the_DTO()
    {
        Variable variable = new VariableBuilder()
            .Named("shift").OfType(VariableType.String).Build();

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("shift")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeNull();
    }

    // ---- spec 014 T028: the refusal paths ----

    [Fact]
    public async Task Another_fabs_variable_is_reported_as_not_found()
    {
        // FR-009: byte-identical to a name that was never used. A 403 would
        // confirm it exists and let an operator enumerate another fab's names
        // one guess at a time.
        Variable foreign = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([foreign]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> notYours = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("oeeLine1")),
            CancellationToken.None);
        Result<VariableDto, GetVariableError> neverExisted = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("ghost")),
            CancellationToken.None);

        notYours.IsFailure.ShouldBeTrue();
        notYours.Error.Code.ShouldBe(neverExisted.Error.Code);
        notYours.Error.Status.ShouldBe(neverExisted.Error.Status);
    }

    [Fact]
    public async Task A_name_held_in_two_of_the_callers_fabs_names_its_candidates()
    {
        Variable munich = new VariableBuilder().WithFab("munich").Named("oeeLine1").Build();
        Variable dresden = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([munich, dresden]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(
                [FabIdentifier.From("munich"), FabIdentifier.From("dresden")],
                VariableName.From("oeeLine1")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        GetVariableError.VariableFabAmbiguous ambiguous =
            result.Error.ShouldBeOfType<GetVariableError.VariableFabAmbiguous>();
        // Naming them leaks nothing: they are all fabs this caller already
        // reads. Without the names the operator cannot act on the message.
        ambiguous.Candidates.ShouldBe(["dresden", "munich"]);
    }

    [Fact]
    public async Task The_dto_carries_the_fab()
    {
        Variable variable = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("dresden")], VariableName.From("oeeLine1")),
            CancellationToken.None);

        result.Value.Fab.ShouldBe("dresden");
    }
}
