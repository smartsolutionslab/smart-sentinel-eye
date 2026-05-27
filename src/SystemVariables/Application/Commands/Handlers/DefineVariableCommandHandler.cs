using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class DefineVariableCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<DefineVariableCommandHandler> log)
    : ICommandHandler<DefineVariableCommand, Result<VariableIdentifier, DefineVariableError>>
{
    public async Task<Result<VariableIdentifier, DefineVariableError>> HandleAsync(
        DefineVariableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Name uniqueness (FR-001 / FR-005). Archived names are free.
        Option<Variable> existing = await variables
            .GetByNameAsync(command.Name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.VariableNameTaken(command.Name.Value));
        }

        // BooleanLabels presence rules. The domain aggregate enforces
        // these too; mapping to typed ApiError happens here so the
        // HTTP layer gets a 400 instead of a 500.
        if (command.Type == VariableType.Boolean && command.BooleanLabels is null)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.BooleanLabelsRequired());
        }
        if (command.Type != VariableType.Boolean && command.BooleanLabels is not null)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.BooleanLabelsOnlyOnBoolean());
        }

        Variable variable;
        try
        {
            variable = Variable.Define(
                command.Name, command.Type, command.InitialValue,
                command.BooleanLabels, command.DefinedBy, clock);
        }
        catch (ArgumentException)
        {
            // The domain aggregate raised on initial-value-vs-type
            // mismatch; remap to a typed ApiError.
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.InitialValueTypeMismatch(command.Type.Value));
        }

        variables.Add(variable);
        await variables.SaveAsync(cancellationToken).ConfigureAwait(false);

        log.LogInformation(
            "Defined variable {Variable} '{Name}' ({Type}) by {Operator}.",
            variable.Id, command.Name, command.Type, command.DefinedBy);

        return Result<VariableIdentifier, DefineVariableError>.Success(variable.Id);
    }
}
