using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class SetVariableValueCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<SetVariableValueCommandHandler> logger)
    : ICommandHandler<SetVariableValueCommand, Result<VariableIdentifier, SetVariableValueError>>
{
    public async Task<Result<VariableIdentifier, SetVariableValueError>> HandleAsync(
        SetVariableValueCommand command,
        CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (FabIdentifier? fab, VariableName? name, string? wireValue, OperatorIdentifier changedBy, Option<int> expectedVersion) = command;

        Option<Variable> found = await variables.GetByNameAsync(fab, name, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(SetVariableValueFailures.VariableNotFound(name.Value));
        }

        Variable variable = found.Value;

        // ADR-0113 Layer 1: refuse an edit built on a view of the variable that
        // has since moved. Checked before any mutation so nothing is applied on
        // top of stale intent.
        //
        // None means the caller holds no prior view to be stale against —
        // automation reacting to an event says "set this to X now", it did not
        // read a value first. Gating that would reject a writer that never had
        // the chance to be wrong. The wire contract is unaffected: the HTTP
        // endpoint still rejects a missing If-Match with 428, so an operator
        // can never reach this branch.
        if (expectedVersion.HasValue && variable.Version != expectedVersion.Value)
        {
            return Failure(SetVariableValueFailures.VariableStale(name.Value, expectedVersion.Value, variable.Version));
        }
        if (variable.State == VariableState.Archived)
        {
            return Failure(SetVariableValueFailures.VariableArchived(name.Value));
        }

        VariableValue typedValue;
        try
        {
            typedValue = VariableValue.From(variable.Type, wireValue);
        }
        catch (ArgumentException ex)
        {
            return Failure(SetVariableValueFailures.VariableTypeMismatch(variable.Type.Value, ex.Message));
        }

        variable.SetValue(typedValue, changedBy, clock);
        await variables.SaveAsync(cancellationToken);

        logger.SetVariable(variable.Id, name, wireValue, changedBy);

        return Success(variable.Id);
    }
}
