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
        ArgumentNullException.ThrowIfNull(command);
        var (name, wireValue, changedBy) = command;

        Option<Variable> found = await variables
            .GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableNotFound(name.Value));
        }

        Variable variable = found.Value;
        if (variable.State == VariableState.Archived)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableArchived(name.Value));
        }

        VariableValue typedValue;
        try
        {
            typedValue = VariableValue.From(variable.Type, wireValue);
        }
        catch (ArgumentException ex)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableTypeMismatch(variable.Type.Value, ex.Message));
        }

        variable.SetValue(typedValue, changedBy, clock);
        await variables.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Set variable {Variable} '{Name}' = '{Value}' by {Operator}.",
            variable.Id, name, wireValue, changedBy);

        return Result<VariableIdentifier, SetVariableValueError>.Success(variable.Id);
    }
}
