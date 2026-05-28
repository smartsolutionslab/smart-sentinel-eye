using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class ListVariablesQueryHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static Variable Define(string name, VariableType type, VariableValue? value = null)
    {
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());
        return Variable.Define(VariableName.From(name), type, value, null, op, clock);
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
        FakeClock clock = new(FixedMoment);
        OperatorIdentifier op = OperatorIdentifier.From(Guid.CreateVersion7());

        Variable defined = Define("active", VariableType.String);
        Variable archived = Define("oldVar", VariableType.String);
        archived.Archive(op, clock);

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
