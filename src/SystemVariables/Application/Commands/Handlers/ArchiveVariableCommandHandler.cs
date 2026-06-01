using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Commands.Handlers;

public sealed class ArchiveVariableCommandHandler(IVariableRepository variables, IClock clock, ILogger<ArchiveVariableCommandHandler> logger)
    : ICommandHandler<ArchiveVariableCommand, Result<VariableIdentifier, ArchiveVariableError>>
{
    public async Task<Result<VariableIdentifier, ArchiveVariableError>> HandleAsync(ArchiveVariableCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        (VariableName? name, OperatorIdentifier archivedBy) = command;

        Option<Variable> found = await variables.GetByNameAsync(name, cancellationToken);
        if (!found.HasValue)
        {
            return Result<VariableIdentifier, ArchiveVariableError>.Failure(new ArchiveVariableError.VariableNotFound(name.Value));
        }

        Variable variable = found.Value;
        variable.Archive(archivedBy, clock);
        await variables.SaveAsync(cancellationToken);

        logger.ArchivedVariable(variable.Id, name, archivedBy);

        return Result<VariableIdentifier, ArchiveVariableError>.Success(variable.Id);
    }
}
