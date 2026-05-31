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
            new GetVariableQuery(VariableName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetVariableError.VariableNotFound>();
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
        Variable variable = new VariableBuilder()
            .Named("shift").OfType(VariableType.String).Build();

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(VariableName.From("shift")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeNull();
    }
}
