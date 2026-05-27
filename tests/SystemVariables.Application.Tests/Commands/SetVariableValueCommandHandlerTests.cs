using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

public class SetVariableValueCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static (InMemoryVariableRepository repo, FakeClock clock, Variable v) Seed(VariableType type)
    {
        InMemoryVariableRepository repo = new();
        FakeClock clock = new(FixedMoment);
        Variable v = Variable.Define(
            VariableName.From("oeeLine1"), type, null,
            type == VariableType.Boolean ? BooleanLabels.Default : null,
            OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        repo.Add(v);
        return (repo, clock, v);
    }

    [Fact]
    public async Task Setting_a_Number_value_succeeds_and_updates_the_value()
    {
        (InMemoryVariableRepository repo, FakeClock clock, Variable v) = Seed(VariableType.Number);

        SetVariableValueCommandHandler handler = new(
            repo, clock, NullLogger<SetVariableValueCommandHandler>.Instance);
        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(
            new SetVariableValueCommand(
                VariableName.From("oeeLine1"), "82.4",
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        v.Value.ShouldBeOfType<VariableValue.NumberValue>().Value.ShouldBe(82.4);
    }

    [Fact]
    public async Task Unknown_variable_returns_VariableNotFound()
    {
        InMemoryVariableRepository repo = new();
        SetVariableValueCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<SetVariableValueCommandHandler>.Instance);

        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(
            new SetVariableValueCommand(
                VariableName.From("ghost"), "1.0",
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SetVariableValueError.VariableNotFound>();
    }

    [Fact]
    public async Task Type_mismatched_value_returns_VariableTypeMismatch()
    {
        (InMemoryVariableRepository repo, FakeClock clock, _) = Seed(VariableType.Number);

        SetVariableValueCommandHandler handler = new(
            repo, clock, NullLogger<SetVariableValueCommandHandler>.Instance);
        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(
            new SetVariableValueCommand(
                VariableName.From("oeeLine1"), "not-a-number",
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<SetVariableValueError.VariableTypeMismatch>();
    }
}
