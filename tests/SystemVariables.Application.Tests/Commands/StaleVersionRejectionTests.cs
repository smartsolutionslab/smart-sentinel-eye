using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Commands;
using SmartSentinelEye.SystemVariables.Application.Commands.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Commands;

/// <summary>
/// ADR-0113 Layer 1 for SystemVariables. Each rejection test also asserts the
/// variable was left alone — the check is only worth having if it runs
/// *before* the mutation, and a handler that rejected afterwards would return
/// the right error while corrupting state.
/// </summary>
public class StaleVersionRejectionTests
{
    private const int Stale = 41;

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-05-27T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Set_value_rejects_a_stale_version_and_keeps_the_stored_value()
    {
        (InMemoryVariableRepository variables, Variable variable) = Seeded();
        string? before = variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString();

        SetVariableValueCommandHandler handler = Setter(variables);
        Result<VariableIdentifier, SetVariableValueError> result = await handler.HandleAsync(
            new SetVariableValueCommand(variable.Fab, variable.Name, "999", Editor(), Option<int>.Some(Stale)),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("VARIABLE_STALE");
        string? after = variable.Value is VariableValue.Unset ? null : variable.Value.ToWireString();
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Archive_rejects_a_stale_version_and_leaves_the_state_alone()
    {
        (InMemoryVariableRepository variables, Variable variable) = Seeded();
        VariableState before = variable.State;

        ArchiveVariableCommandHandler handler = new(
            variables, new FakeClock(FixedMoment), NullLogger<ArchiveVariableCommandHandler>.Instance);
        Result<VariableIdentifier, ArchiveVariableError> result = await handler.HandleAsync(
            new ArchiveVariableCommand(variable.Name, Editor(), Stale),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("VARIABLE_STALE");
        variable.State.ShouldBe(before);
    }

    [Fact]
    public async Task The_matching_version_is_accepted()
    {
        (InMemoryVariableRepository variables, Variable variable) = Seeded();

        Result<VariableIdentifier, SetVariableValueError> result = await Setter(variables).HandleAsync(
            new SetVariableValueCommand(variable.Fab, variable.Name, "12", Editor(), Option<int>.Some(variable.Version)),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Automation reacting to an event holds no prior view to be stale
    /// against — it says "set this to X now", it did not read a value first.
    /// Gating it would reject a writer that never had the chance to be wrong.
    /// The wire contract is unaffected: the HTTP endpoint still rejects a
    /// missing If-Match with 428, so an operator cannot reach this path.
    /// </summary>
    [Fact]
    public async Task A_caller_with_no_prior_view_is_not_gated()
    {
        (InMemoryVariableRepository variables, Variable variable) = Seeded();

        Result<VariableIdentifier, SetVariableValueError> result = await Setter(variables).HandleAsync(
            new SetVariableValueCommand(variable.Fab, variable.Name, "77", Editor(), Option<int>.None),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        variable.Value.ToWireString().ShouldBe("77");
    }

    private static SetVariableValueCommandHandler Setter(InMemoryVariableRepository variables) =>
        new(variables, new FakeClock(FixedMoment), NullLogger<SetVariableValueCommandHandler>.Instance);

    private static OperatorIdentifier Editor() => OperatorIdentifier.From(Guid.CreateVersion7());

    private static (InMemoryVariableRepository, Variable) Seeded()
    {
        InMemoryVariableRepository variables = new();
        Variable variable = new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(42)).Build();
        variables.Add(variable);

        return (variables, variable);
    }
}
