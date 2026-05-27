using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class SetVariableValueCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<SetVariableValueCommandHandler> log)
    : ICommandHandler<SetVariableValueCommand, Result<VariableIdentifier, SetVariableValueError>>
{
    public async Task<Result<VariableIdentifier, SetVariableValueError>> HandleAsync(
        SetVariableValueCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Option<Variable> found = await variables
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (!found.HasValue)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableNotFound(command.Name.Value));
        }

        Variable variable = found.Value;
        if (variable.State == VariableState.Archived)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableArchived(command.Name.Value));
        }

        VariableValue typedValue;
        try
        {
            typedValue = VariableValue.From(variable.Type, command.WireValue);
        }
        catch (ArgumentException ex)
        {
            return Result<VariableIdentifier, SetVariableValueError>.Failure(
                new SetVariableValueError.VariableTypeMismatch(variable.Type.Value, ex.Message));
        }

        variable.SetValue(typedValue, command.ChangedBy, clock);
        await variables.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Set variable {Variable} '{Name}' = '{Value}' by {Operator}.",
            variable.Id, command.Name, command.WireValue, command.ChangedBy);

        return Result<VariableIdentifier, SetVariableValueError>.Success(variable.Id);
    }
}
