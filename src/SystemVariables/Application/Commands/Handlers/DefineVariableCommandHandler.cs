using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class DefineVariableCommandHandler(
    IVariableRepository variables,
    IClock clock,
    ILogger<DefineVariableCommandHandler> logger)
    : ICommandHandler<DefineVariableCommand, Result<VariableIdentifier, DefineVariableError>>
{
    public async Task<Result<VariableIdentifier, DefineVariableError>> HandleAsync(
        DefineVariableCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (name, type, initialValue, booleanLabels, definedBy) = command;

        // Name uniqueness (FR-001 / FR-005). Archived names are free.
        Option<Variable> existing = await variables
            .GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);
        if (existing.HasValue)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.VariableNameTaken(name.Value));
        }

        // BooleanLabels presence rules. The domain aggregate enforces
        // these too; mapping to typed ApiError happens here so the
        // HTTP layer gets a 400 instead of a 500.
        if (type == VariableType.Boolean && booleanLabels is null)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.BooleanLabelsRequired());
        }
        if (type != VariableType.Boolean && booleanLabels is not null)
        {
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.BooleanLabelsOnlyOnBoolean());
        }

        Variable variable;
        try
        {
            variable = Variable.Define(
                name, type, initialValue,
                booleanLabels, definedBy, clock);
        }
        catch (ArgumentException)
        {
            // The domain aggregate raised on initial-value-vs-type
            // mismatch; remap to a typed ApiError.
            return Result<VariableIdentifier, DefineVariableError>.Failure(
                new DefineVariableError.InitialValueTypeMismatch(type.Value));
        }

        variables.Add(variable);
        await variables.SaveAsync(cancellationToken).ConfigureAwait(false);

        Log.DefinedVariable(logger, variable.Id, name, type, definedBy);

        return Result<VariableIdentifier, DefineVariableError>.Success(variable.Id);
    }
}
