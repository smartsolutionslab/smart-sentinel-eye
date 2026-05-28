using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class GetVariableQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Returns_VariableNotFound_when_no_variable_with_that_name_exists()
    {
        TestVariableQuerySource source = new([]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(VariableName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetVariableError.VariableNotFound>();
    }

    [Fact]
    public async Task Returns_a_mapped_DTO_when_the_variable_exists()
    {
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier definer = OperatorIdentifier.From(Guid.CreateVersion7());
        Variable variable = Variable.Define(
            VariableName.From("oeeLine1"), VariableType.Number,
            new VariableValue.NumberValue(82.5), null, definer, clock);

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(VariableName.From("oeeLine1")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("oeeLine1");
        result.Value.Type.ShouldBe("Number");
        result.Value.State.ShouldBe("Defined");
        result.Value.Value.ShouldBe("82.5");
    }

    [Fact]
    public async Task Maps_Unset_value_to_null_on_the_DTO()
    {
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier definer = OperatorIdentifier.From(Guid.CreateVersion7());
        Variable variable = Variable.Define(
            VariableName.From("shift"), VariableType.String, null, null, definer, clock);

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(VariableName.From("shift")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeNull();
    }
}
