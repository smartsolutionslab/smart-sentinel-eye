using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

public class DefineVariableCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task First_definition_with_a_unique_name_returns_a_new_VariableIdentifier()
    {
        InMemoryVariableRepository repo = new();
        DefineVariableCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<DefineVariableCommandHandler>.Instance);

        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(
            new DefineVariableCommand(
                VariableName.From("oeeLine1"),
                VariableType.Number,
                InitialValue: null,
                BooleanLabels: null,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        repo.Variables.Count.ShouldBe(1);
        repo.Variables[0].Value.ShouldBeOfType<VariableValue.Unset>();
    }

    [Fact]
    public async Task Name_collision_with_a_non_archived_variable_returns_VariableNameTaken()
    {
        InMemoryVariableRepository repo = new();
        FakeClock clock = new(FixedMoment);
        Variable existing = Variable.Define(
            VariableName.From("oeeLine1"), VariableType.Number, null, null,
            OperatorIdentifier.From(Guid.CreateVersion7()), clock);
        repo.Add(existing);

        DefineVariableCommandHandler handler = new(repo, clock, NullLogger<DefineVariableCommandHandler>.Instance);
        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(
            new DefineVariableCommand(
                VariableName.From("oeeLine1"),
                VariableType.Number,
                InitialValue: null,
                BooleanLabels: null,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DefineVariableError.VariableNameTaken>();
    }

    [Fact]
    public async Task Boolean_without_BooleanLabels_returns_BooleanLabelsRequired()
    {
        InMemoryVariableRepository repo = new();
        DefineVariableCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<DefineVariableCommandHandler>.Instance);

        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(
            new DefineVariableCommand(
                VariableName.From("lineState"),
                VariableType.Boolean,
                InitialValue: null,
                BooleanLabels: null,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DefineVariableError.BooleanLabelsRequired>();
    }

    [Fact]
    public async Task Non_Boolean_with_BooleanLabels_returns_BooleanLabelsOnlyOnBoolean()
    {
        InMemoryVariableRepository repo = new();
        DefineVariableCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<DefineVariableCommandHandler>.Instance);

        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(
            new DefineVariableCommand(
                VariableName.From("oeeLine1"),
                VariableType.Number,
                InitialValue: null,
                BooleanLabels: BooleanLabels.Default,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DefineVariableError.BooleanLabelsOnlyOnBoolean>();
    }

    [Fact]
    public async Task Type_mismatched_initial_value_returns_InitialValueTypeMismatch()
    {
        InMemoryVariableRepository repo = new();
        DefineVariableCommandHandler handler = new(
            repo, new FakeClock(FixedMoment), NullLogger<DefineVariableCommandHandler>.Instance);

        Result<VariableIdentifier, DefineVariableError> result = await handler.HandleAsync(
            new DefineVariableCommand(
                VariableName.From("oeeLine1"),
                VariableType.Number,
                InitialValue: new VariableValue.StringValue("nope"),
                BooleanLabels: null,
                OperatorIdentifier.From(Guid.CreateVersion7())),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<DefineVariableError.InitialValueTypeMismatch>();
    }
}
