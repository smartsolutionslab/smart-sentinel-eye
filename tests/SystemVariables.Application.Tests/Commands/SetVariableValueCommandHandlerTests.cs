using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

public class SetVariableValueCommandHandlerTests
{
    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    private static (InMemoryVariableRepository repo, IClock clock, Variable v) Seed(VariableType type)
    {
        InMemoryVariableRepository repo = new();
        VariableBuilder builder = new VariableBuilder().Named("oeeLine1").OfType(type);
        if (type == VariableType.Boolean)
        {
            builder.WithBooleanLabels(BooleanLabels.Default);
        }

        Variable v = builder.Build();
        repo.Add(v);
        return (repo, builder.Clock, v);
    }

    [Fact]
    public async Task Setting_a_Number_value_succeeds_and_updates_the_value()
    {
        (InMemoryVariableRepository repo, IClock clock, Variable v) = Seed(VariableType.Number);

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
        (InMemoryVariableRepository repo, IClock clock, _) = Seed(VariableType.Number);

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
