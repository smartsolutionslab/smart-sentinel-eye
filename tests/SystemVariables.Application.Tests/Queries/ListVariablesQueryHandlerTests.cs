using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class ListVariablesQueryHandlerTests
{
    private static Variable Define(string name, VariableType type, VariableValue? value = null)
    {
        VariableBuilder builder = new VariableBuilder().Named(name).OfType(type);
        if (value is not null)
        {
            builder.WithInitialValue(value);
        }

        return builder.Build();
    }

    [Fact]
    public async Task Returns_empty_list_when_no_variables_exist()
    {
        ListVariablesQueryHandler handler = new(new TestVariableQuerySource([]));

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler.HandleAsync(
            new ListVariablesQuery(State: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_all_variables_sorted_by_name_when_no_state_filter_is_given()
    {
        Variable[] seeded =
        [
            Define("zulu", VariableType.String),
            Define("alpha", VariableType.Number, new VariableValue.NumberValue(1.0)),
            Define("mike", VariableType.String),
        ];
        ListVariablesQueryHandler handler = new(new TestVariableQuerySource(seeded));

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler.HandleAsync(
            new ListVariablesQuery(State: null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(v => v.Name).ShouldBe(["alpha", "mike", "zulu"]);
    }

    [Fact]
    public async Task Filters_by_state_when_a_state_is_provided()
    {
        VariableBuilder archivedBuilder = new VariableBuilder().Named("oldVar").OfType(VariableType.String);

        Variable defined = Define("active", VariableType.String);
        Variable archived = archivedBuilder.Build();
        archived.Archive(archivedBuilder.Operator, archivedBuilder.Clock);

        ListVariablesQueryHandler handler = new(
            new TestVariableQuerySource([defined, archived]));

        Result<IReadOnlyList<VariableDto>, ListVariablesError> result = await handler.HandleAsync(
            new ListVariablesQuery(State: VariableState.Archived), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        VariableDto only = result.Value.ShouldHaveSingleItem();
        only.Name.ShouldBe("oldVar");
        only.State.ShouldBe("Archived");
    }
}
